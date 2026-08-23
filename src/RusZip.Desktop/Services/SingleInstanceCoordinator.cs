using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;

namespace RusZip.Desktop.Services;

/// <summary>
/// Implements single-instance IPC coordination for RusZip Desktop using Windows Named Pipes
/// or Unix Domain Sockets on Linux and macOS.
/// </summary>
public sealed class SingleInstanceCoordinator : ISingleInstanceCoordinator
{
    private const byte AckByte = 0x06; // ASCII ACK
    private const int MaxPayloadLength = 1024 * 1024; // 1 MB sanity limit

    private readonly object _syncLock = new();
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private Action<string?>? _onFileReceived;
    private Socket? _serverSocket;
    private bool _isListening;
    private bool _disposed;

    /// <summary>
    /// Gets the pipe name used on Windows.
    /// </summary>
    public string PipeName { get; }

    /// <summary>
    /// Gets the socket file path used on Linux and macOS.
    /// </summary>
    public string SocketPath { get; }

    /// <summary>
    /// Gets a value indicating whether the coordinator is currently listening for incoming connections.
    /// </summary>
    public bool IsListening
    {
        get
        {
            lock (_syncLock)
            {
                return _isListening;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleInstanceCoordinator"/> class using default OS identifiers.
    /// </summary>
    public SingleInstanceCoordinator()
        : this(identifier: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleInstanceCoordinator"/> class using a custom identifier suffix.
    /// </summary>
    /// <param name="identifier">Custom identifier used to scope pipe and socket names (useful for tests or isolation).</param>
    public SingleInstanceCoordinator(string? identifier)
    {
        var sanitized = SanitizeIdentifier(identifier ?? Environment.UserName);
        PipeName = $"RusZip_SingleInstance_{sanitized}";
        SocketPath = Path.Combine(GetDefaultSocketDirectory(), $"ruszip_{sanitized}.sock");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleInstanceCoordinator"/> class with explicit pipe and socket paths.
    /// </summary>
    /// <param name="pipeName">Custom named pipe name for Windows.</param>
    /// <param name="socketPath">Custom unix domain socket path for Linux/macOS.</param>
    public SingleInstanceCoordinator(string? pipeName, string? socketPath)
    {
        var sanitized = SanitizeIdentifier(Environment.UserName);
        PipeName = pipeName ?? $"RusZip_SingleInstance_{sanitized}";
        SocketPath = socketPath ?? Path.Combine(GetDefaultSocketDirectory(), $"ruszip_{sanitized}.sock");
    }

    /// <inheritdoc />
    public async Task<bool> TrySendToExistingInstanceAsync(string? filePath, CancellationToken ct = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return await TrySendWindowsNamedPipeAsync(filePath, ct);
        }
        else
        {
            return await TrySendUnixDomainSocketAsync(filePath, ct);
        }
    }

    /// <inheritdoc />
    public void StartListening(Action<string?> onFileReceived)
    {
        ArgumentNullException.ThrowIfNull(onFileReceived);

        lock (_syncLock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SingleInstanceCoordinator));
            }

            if (_isListening)
            {
                return;
            }

            _onFileReceived = onFileReceived;
            _cts = new CancellationTokenSource();
            _isListening = true;

            if (OperatingSystem.IsWindows())
            {
                _listenerTask = Task.Run(() => ListenWindowsNamedPipeAsync(_cts.Token));
            }
            else
            {
                try
                {
                    if (File.Exists(SocketPath))
                    {
                        try { File.Delete(SocketPath); } catch { /* Ignore */ }
                    }

                    var dir = Path.GetDirectoryName(SocketPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var serverSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    serverSocket.Bind(new UnixDomainSocketEndPoint(SocketPath));
                    serverSocket.Listen(10);
                    _serverSocket = serverSocket;

                    _listenerTask = Task.Run(() => ListenUnixDomainSocketAsync(serverSocket, _cts.Token));
                }
                catch (Exception)
                {
                    _isListening = false;
                    _serverSocket?.Dispose();
                    _serverSocket = null;
                }
            }
        }
    }

    /// <inheritdoc />
    public void StopListening()
    {
        lock (_syncLock)
        {
            if (!_isListening)
            {
                return;
            }

            _isListening = false;
            _cts?.Cancel();

            try
            {
                _serverSocket?.Close();
                _serverSocket?.Dispose();
                _serverSocket = null;
            }
            catch { /* Ignore */ }

            if (!OperatingSystem.IsWindows() && File.Exists(SocketPath))
            {
                try { File.Delete(SocketPath); } catch { /* Ignore */ }
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_disposed) return;
            _disposed = true;

            StopListening();
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Task? listenerTask;
        lock (_syncLock)
        {
            if (_disposed) return;
            _disposed = true;

            listenerTask = _listenerTask;
            StopListening();
        }

        if (listenerTask != null)
        {
            try
            {
                await listenerTask;
            }
            catch { /* Ignore cancellation or exit exceptions */ }
        }

        lock (_syncLock)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task<bool> TrySendUnixDomainSocketAsync(string? filePath, CancellationToken ct)
    {
        if (!File.Exists(SocketPath))
        {
            return false;
        }

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(2));

            var endpoint = new UnixDomainSocketEndPoint(SocketPath);
            await socket.ConnectAsync(endpoint, linkedCts.Token);

            using var stream = new NetworkStream(socket, ownsSocket: false);
            await SendPayloadAndAwaitAckAsync(stream, filePath, linkedCts.Token);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<bool> TrySendWindowsNamedPipeAsync(string? filePath, CancellationToken ct)
    {
        try
        {
            using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(2));

            await pipeClient.ConnectAsync(linkedCts.Token);
            await SendPayloadAndAwaitAckAsync(pipeClient, filePath, linkedCts.Token);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task ListenUnixDomainSocketAsync(Socket serverSocket, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await serverSocket.AcceptAsync(ct);
                _ = HandleUnixClientAsync(clientSocket, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception)
            {
                if (ct.IsCancellationRequested) break;
            }
        }
    }

    private async Task HandleUnixClientAsync(Socket clientSocket, CancellationToken ct)
    {
        using (clientSocket)
        {
            try
            {
                using var stream = new NetworkStream(clientSocket, ownsSocket: false);
                var filePath = await ReadPayloadAndSendAckAsync(stream, ct);
                _onFileReceived?.Invoke(filePath);
            }
            catch
            {
                // Client disconnected or transmission failed
            }
        }
    }

    private async Task ListenWindowsNamedPipeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? serverStream = null;
            try
            {
                serverStream = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await serverStream.WaitForConnectionAsync(ct);

                var activeStream = serverStream;
                serverStream = null;

                _ = HandleNamedPipeClientAsync(activeStream, ct);
            }
            catch (OperationCanceledException)
            {
                serverStream?.Dispose();
                break;
            }
            catch (ObjectDisposedException)
            {
                serverStream?.Dispose();
                break;
            }
            catch (Exception)
            {
                serverStream?.Dispose();
                if (ct.IsCancellationRequested) break;
            }
        }
    }

    private async Task HandleNamedPipeClientAsync(NamedPipeServerStream stream, CancellationToken ct)
    {
        using (stream)
        {
            try
            {
                var filePath = await ReadPayloadAndSendAckAsync(stream, ct);
                _onFileReceived?.Invoke(filePath);
            }
            catch
            {
                // Client disconnected or transmission failed
            }
        }
    }

    private static async Task SendPayloadAndAwaitAckAsync(Stream stream, string? filePath, CancellationToken ct)
    {
        byte[] payloadBytes = string.IsNullOrEmpty(filePath) ? [] : Encoding.UTF8.GetBytes(filePath);
        byte[] lengthBytes = BitConverter.GetBytes(payloadBytes.Length);

        await stream.WriteAsync(lengthBytes, ct);
        if (payloadBytes.Length > 0)
        {
            await stream.WriteAsync(payloadBytes, ct);
        }
        await stream.FlushAsync(ct);

        byte[] ackBuffer = new byte[1];
        int bytesRead = await stream.ReadAsync(ackBuffer, ct);
        if (bytesRead <= 0 || ackBuffer[0] != AckByte)
        {
            throw new IOException("Did not receive valid ACK from primary instance.");
        }
    }

    private static async Task<string?> ReadPayloadAndSendAckAsync(Stream stream, CancellationToken ct)
    {
        byte[] lengthBuffer = new byte[4];
        await ReadExactAsync(stream, lengthBuffer, 0, 4, ct);
        int length = BitConverter.ToInt32(lengthBuffer, 0);

        if (length < 0 || length > MaxPayloadLength)
        {
            throw new InvalidDataException($"Invalid IPC payload length: {length}");
        }

        string? filePath = null;
        if (length > 0)
        {
            byte[] payloadBuffer = new byte[length];
            await ReadExactAsync(stream, payloadBuffer, 0, length, ct);
            filePath = Encoding.UTF8.GetString(payloadBuffer);
        }

        await stream.WriteAsync(new[] { AckByte }, ct);
        await stream.FlushAsync(ct);

        return filePath;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct);
            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading IPC payload.");
            }
            totalRead += read;
        }
    }

    private static string GetDefaultSocketDirectory()
    {
        return Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath();
    }

    private static string SanitizeIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return "user";
        }

        var chars = identifier.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray();
        var sanitized = new string(chars);
        return string.IsNullOrWhiteSpace(sanitized) ? "user" : sanitized;
    }
}

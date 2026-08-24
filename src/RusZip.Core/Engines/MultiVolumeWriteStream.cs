namespace RusZip.Core.Engines;

/// <summary>
/// Output stream that automatically slices continuous byte streams into fixed-size sequential volumes.
/// Formats part files as &lt;base&gt;.part&lt;N&gt;.&lt;ext&gt; (e.g. archive.part1.zrus, archive.part2.zrus).
/// Minimum volume threshold is 64 KB (65,536 bytes).
/// </summary>
public sealed class MultiVolumeWriteStream : Stream
{
    public const long MinimumVolumeBytes = 65536; // 64 KB
    private const int BufferSize = 81920;

    private readonly string _destinationPath;
    private readonly long _maxVolumeBytes;
    private readonly List<string> _createdVolumePaths = [];
    private FileStream? _currentStream;
    private long _currentVolumeBytesWritten;
    private int _currentPartIndex;
    private bool _isDisposed;

    public IReadOnlyList<string> CreatedVolumePaths => _createdVolumePaths;
    public int CurrentVolumeIndex => _currentPartIndex;
    public string? CurrentVolumePath => _createdVolumePaths.Count > 0 ? _createdVolumePaths[^1] : null;

    public Action<int, string>? VolumeChanged { get; set; }

    public MultiVolumeWriteStream(string destinationPath, long maxVolumeBytes)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Destination path cannot be empty.", nameof(destinationPath));
        }

        if (maxVolumeBytes < MinimumVolumeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxVolumeBytes),
                maxVolumeBytes,
                $"Split volume size must be at least {MinimumVolumeBytes:N0} bytes (64 KB).");
        }

        _destinationPath = Path.GetFullPath(destinationPath);
        _maxVolumeBytes = maxVolumeBytes;
        _currentPartIndex = 1;

        OpenNextVolume();
    }

    private void OpenNextVolume()
    {
        _currentStream?.Dispose();

        var volumePath = VolumeNameResolver.GetVolumePath(_destinationPath, _currentPartIndex);
        var dir = Path.GetDirectoryName(volumePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _currentStream = new FileStream(
            volumePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true);

        _createdVolumePaths.Add(volumePath);
        _currentVolumeBytesWritten = 0;
        VolumeChanged?.Invoke(_currentPartIndex, volumePath);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        while (count > 0)
        {
            long remainingInVolume = _maxVolumeBytes - _currentVolumeBytesWritten;
            if (remainingInVolume <= 0)
            {
                _currentPartIndex++;
                OpenNextVolume();
                remainingInVolume = _maxVolumeBytes;
            }

            int toWrite = (int)Math.Min(count, remainingInVolume);
            _currentStream!.Write(buffer, offset, toWrite);
            _currentVolumeBytesWritten += toWrite;
            offset += toWrite;
            count -= toWrite;
        }
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        while (count > 0)
        {
            long remainingInVolume = _maxVolumeBytes - _currentVolumeBytesWritten;
            if (remainingInVolume <= 0)
            {
                _currentPartIndex++;
                OpenNextVolume();
                remainingInVolume = _maxVolumeBytes;
            }

            int toWrite = (int)Math.Min(count, remainingInVolume);
            await _currentStream!.WriteAsync(buffer.AsMemory(offset, toWrite), cancellationToken);
            _currentVolumeBytesWritten += toWrite;
            offset += toWrite;
            count -= toWrite;
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        while (buffer.Length > 0)
        {
            long remainingInVolume = _maxVolumeBytes - _currentVolumeBytesWritten;
            if (remainingInVolume <= 0)
            {
                _currentPartIndex++;
                OpenNextVolume();
                remainingInVolume = _maxVolumeBytes;
            }

            int toWrite = (int)Math.Min(buffer.Length, remainingInVolume);
            await _currentStream!.WriteAsync(buffer[..toWrite], cancellationToken);
            _currentVolumeBytesWritten += toWrite;
            buffer = buffer[toWrite..];
        }
    }

    public override void Flush()
    {
        _currentStream?.Flush();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_currentStream != null)
        {
            await _currentStream.FlushAsync(cancellationToken);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                _currentStream?.Dispose();
                _currentStream = null;
            }
            _isDisposed = true;
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            if (_currentStream != null)
            {
                await _currentStream.DisposeAsync();
                _currentStream = null;
            }
            _isDisposed = true;
        }
        await base.DisposeAsync();
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

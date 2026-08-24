using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace RusZip.Core.Engines;

/// <summary>
/// Authenticated AES-256-GCM encryption envelope for .zrus native archives.
/// Binary header: "zenc" (4B) + Version (1B) + Salt (16B) + KDF Iterations (4B) + Master Nonce (12B) + Password Verification Tag (16B) = 53 Bytes.
/// </summary>
public static class ZrusCryptoEnvelope
{
    public static readonly byte[] Magic = [0x7A, 0x65, 0x6E, 0x63]; // "zenc"
    public const byte CurrentVersion = 0x01;
    public const int DefaultIterations = 100_000;
    public const int SaltSize = 16;
    public const int MasterNonceSize = 12;
    public const int TagSize = 16;
    public const int HeaderSize = 4 + 1 + 16 + 4 + 12 + 16; // 53 bytes
    public const int ChunkPayloadSize = 65536; // 64 KB
    public static readonly byte[] AuthCheckPayload = Encoding.UTF8.GetBytes("RUSZIP_AUTH_CHECK");

    public static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32 // 256-bit AES key
        );
    }

    public static byte[] ComputePasswordVerificationTag(byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(AuthCheckPayload)[..TagSize];
    }

    public static bool IsEncrypted(Stream stream)
    {
        if (!stream.CanRead) return false;

        Span<byte> header = stackalloc byte[4];
        long originalPos = stream.CanSeek ? stream.Position : 0;

        int totalRead = 0;
        while (totalRead < 4)
        {
            int read = stream.Read(header[totalRead..]);
            if (read == 0) break;
            totalRead += read;
        }

        if (stream.CanSeek)
        {
            stream.Seek(originalPos, SeekOrigin.Begin);
        }

        return totalRead == 4 && header.SequenceEqual(Magic);
    }

    public static bool IsEncryptedFile(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return IsEncrypted(fs);
        }
        catch
        {
            return false;
        }
    }

    public static Stream CreateEncryptionStream(Stream targetStream, string password, bool leaveOpen = false)
    {
        return new ZrusEncryptionStream(targetStream, password, leaveOpen);
    }

    public static Stream CreateDecryptionStream(Stream sourceStream, string password, bool leaveOpen = false)
    {
        return new ZrusDecryptionStream(sourceStream, password, leaveOpen);
    }
}

/// <summary>
/// Streaming AES-256-GCM chunked writer for .zrus archives.
/// </summary>
public sealed class ZrusEncryptionStream : Stream
{
    private readonly Stream _targetStream;
    private readonly AesGcm _aesGcm;
    private readonly byte[] _key;
    private readonly byte[] _masterNonce;
    private readonly bool _leaveOpen;
    private readonly byte[] _buffer = new byte[ZrusCryptoEnvelope.ChunkPayloadSize];
    private int _bufferedCount;
    private ulong _chunkIndex;
    private bool _isDisposed;

    public ZrusEncryptionStream(Stream targetStream, string password, bool leaveOpen = false)
    {
        _targetStream = targetStream ?? throw new ArgumentNullException(nameof(targetStream));
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password cannot be empty.", nameof(password));

        _leaveOpen = leaveOpen;

        // Generate salt & master nonce
        var salt = RandomNumberGenerator.GetBytes(ZrusCryptoEnvelope.SaltSize);
        _masterNonce = RandomNumberGenerator.GetBytes(ZrusCryptoEnvelope.MasterNonceSize);

        _key = ZrusCryptoEnvelope.DeriveKey(password, salt, ZrusCryptoEnvelope.DefaultIterations);
        _aesGcm = new AesGcm(_key, ZrusCryptoEnvelope.TagSize);

        var verificationTag = ZrusCryptoEnvelope.ComputePasswordVerificationTag(_key);

        WriteHeader(salt, ZrusCryptoEnvelope.DefaultIterations, _masterNonce, verificationTag);
    }

    private void WriteHeader(byte[] salt, int iterations, byte[] masterNonce, byte[] verificationTag)
    {
        _targetStream.Write(ZrusCryptoEnvelope.Magic);
        _targetStream.WriteByte(ZrusCryptoEnvelope.CurrentVersion);
        _targetStream.Write(salt);

        Span<byte> iterBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(iterBytes, iterations);
        _targetStream.Write(iterBytes);

        _targetStream.Write(masterNonce);
        _targetStream.Write(verificationTag);
    }

    private void DeriveChunkNonce(ulong chunkIndex, Span<byte> destinationNonce)
    {
        _masterNonce.AsSpan().CopyTo(destinationNonce);
        // XOR chunkIndex into nonce to ensure unique nonces per chunk
        var counter = BinaryPrimitives.ReadUInt64LittleEndian(destinationNonce[4..]);
        BinaryPrimitives.WriteUInt64LittleEndian(destinationNonce[4..], counter ^ chunkIndex);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        while (count > 0)
        {
            int toCopy = Math.Min(count, _buffer.Length - _bufferedCount);
            Buffer.BlockCopy(buffer, offset, _buffer, _bufferedCount, toCopy);
            _bufferedCount += toCopy;
            offset += toCopy;
            count -= toCopy;

            if (_bufferedCount == _buffer.Length)
            {
                FlushChunk();
            }
        }
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        while (count > 0)
        {
            int toCopy = Math.Min(count, _buffer.Length - _bufferedCount);
            Buffer.BlockCopy(buffer, offset, _buffer, _bufferedCount, toCopy);
            _bufferedCount += toCopy;
            offset += toCopy;
            count -= toCopy;

            if (_bufferedCount == _buffer.Length)
            {
                await FlushChunkAsync(cancellationToken);
            }
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        while (buffer.Length > 0)
        {
            int toCopy = Math.Min(buffer.Length, _buffer.Length - _bufferedCount);
            buffer[..toCopy].Span.CopyTo(_buffer.AsSpan(_bufferedCount));
            _bufferedCount += toCopy;
            buffer = buffer[toCopy..];

            if (_bufferedCount == _buffer.Length)
            {
                await FlushChunkAsync(cancellationToken);
            }
        }
    }

    private void FlushChunk()
    {
        if (_bufferedCount == 0) return;

        var plaintext = _buffer.AsSpan(0, _bufferedCount);
        Span<byte> ciphertext = stackalloc byte[_bufferedCount];
        Span<byte> tag = stackalloc byte[ZrusCryptoEnvelope.TagSize];
        Span<byte> nonce = stackalloc byte[ZrusCryptoEnvelope.MasterNonceSize];

        DeriveChunkNonce(_chunkIndex++, nonce);

        _aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        // Write Chunk Length (4B)
        Span<byte> lenBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBytes, _bufferedCount);
        _targetStream.Write(lenBytes);

        // Write Nonce (12B)
        _targetStream.Write(nonce);

        // Write Ciphertext (NB)
        _targetStream.Write(ciphertext);

        // Write Tag (16B)
        _targetStream.Write(tag);

        _bufferedCount = 0;
    }

    private async Task FlushChunkAsync(CancellationToken cancellationToken)
    {
        if (_bufferedCount == 0) return;

        var plaintext = _buffer.AsSpan(0, _bufferedCount);
        var ciphertext = new byte[_bufferedCount];
        var tag = new byte[ZrusCryptoEnvelope.TagSize];
        var nonce = new byte[ZrusCryptoEnvelope.MasterNonceSize];

        DeriveChunkNonce(_chunkIndex++, nonce);

        _aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        byte[] lenBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBytes, _bufferedCount);

        await _targetStream.WriteAsync(lenBytes, cancellationToken);
        await _targetStream.WriteAsync(nonce, cancellationToken);
        await _targetStream.WriteAsync(ciphertext, cancellationToken);
        await _targetStream.WriteAsync(tag, cancellationToken);

        _bufferedCount = 0;
    }

    public override void Flush()
    {
        FlushChunk();
        _targetStream.Flush();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await FlushChunkAsync(cancellationToken);
        await _targetStream.FlushAsync(cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                FlushChunk();
                // Write EOF chunk marker (Length = 0)
                Span<byte> eofMarker = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(eofMarker, 0);
                _targetStream.Write(eofMarker);
                _targetStream.Flush();

                _aesGcm.Dispose();
                CryptographicOperations.ZeroMemory(_key);

                if (!_leaveOpen)
                {
                    _targetStream.Dispose();
                }
            }
            _isDisposed = true;
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            await FlushChunkAsync(CancellationToken.None);

            byte[] eofMarker = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(eofMarker, 0);
            await _targetStream.WriteAsync(eofMarker);
            await _targetStream.FlushAsync();

            _aesGcm.Dispose();
            CryptographicOperations.ZeroMemory(_key);

            if (!_leaveOpen)
            {
                await _targetStream.DisposeAsync();
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

/// <summary>
/// Streaming AES-256-GCM chunked reader for .zrus archives with instant password verification.
/// </summary>
public sealed class ZrusDecryptionStream : Stream
{
    private readonly Stream _sourceStream;
    private readonly AesGcm _aesGcm;
    private readonly byte[] _key;
    private readonly bool _leaveOpen;
    private byte[]? _currentPlaintextChunk;
    private int _currentChunkOffset;
    private bool _isEndOfStream;
    private bool _isDisposed;

    public ZrusDecryptionStream(Stream sourceStream, string password, bool leaveOpen = false)
    {
        _sourceStream = sourceStream ?? throw new ArgumentNullException(nameof(sourceStream));
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password cannot be empty.", nameof(password));

        _leaveOpen = leaveOpen;

        // Read and validate 53-byte header
        Span<byte> header = stackalloc byte[ZrusCryptoEnvelope.HeaderSize];
        int read = ReadExact(sourceStream, header);
        if (read < ZrusCryptoEnvelope.HeaderSize || !header[..4].SequenceEqual(ZrusCryptoEnvelope.Magic))
        {
            throw new ArchiveIntegrityException("The archive is not a valid encrypted .zrus file.");
        }

        var version = header[4];
        if (version != ZrusCryptoEnvelope.CurrentVersion)
        {
            throw new ArchiveIntegrityException($"Unsupported encrypted archive version: {version}");
        }

        var salt = header[5..21].ToArray();
        var iterations = BinaryPrimitives.ReadInt32LittleEndian(header[21..25]);
        var masterNonce = header[25..37].ToArray();
        var verificationTag = header[37..53].ToArray();

        _key = ZrusCryptoEnvelope.DeriveKey(password, salt, iterations);

        // Immediate password verification check
        var computedTag = ZrusCryptoEnvelope.ComputePasswordVerificationTag(_key);
        if (!CryptographicOperations.FixedTimeEquals(computedTag, verificationTag))
        {
            CryptographicOperations.ZeroMemory(_key);
            throw new ArchiveIntegrityException("Invalid archive password.");
        }

        _aesGcm = new AesGcm(_key, ZrusCryptoEnvelope.TagSize);
    }

    private static int ReadExact(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int r = stream.Read(buffer[total..]);
            if (r == 0) break;
            total += r;
        }
        return total;
    }

    private static async Task<int> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int r = await stream.ReadAsync(buffer[total..], ct);
            if (r == 0) break;
            total += r;
        }
        return total;
    }

    private bool ReadNextChunk()
    {
        if (_isEndOfStream) return false;

        Span<byte> lenBytes = stackalloc byte[4];
        int read = ReadExact(_sourceStream, lenBytes);
        if (read < 4)
        {
            _isEndOfStream = true;
            return false;
        }

        int chunkLen = BinaryPrimitives.ReadInt32LittleEndian(lenBytes);
        if (chunkLen == 0) // EOF marker
        {
            _isEndOfStream = true;
            return false;
        }

        if (chunkLen < 0 || chunkLen > ZrusCryptoEnvelope.ChunkPayloadSize * 2)
        {
            throw new ArchiveIntegrityException("Corrupt chunk length in encrypted archive.");
        }

        Span<byte> nonce = stackalloc byte[ZrusCryptoEnvelope.MasterNonceSize];
        if (ReadExact(_sourceStream, nonce) < ZrusCryptoEnvelope.MasterNonceSize)
        {
            throw new ArchiveIntegrityException("Unexpected end of stream while reading chunk nonce.");
        }

        var ciphertext = new byte[chunkLen];
        if (ReadExact(_sourceStream, ciphertext) < chunkLen)
        {
            throw new ArchiveIntegrityException("Unexpected end of stream while reading chunk ciphertext.");
        }

        Span<byte> tag = stackalloc byte[ZrusCryptoEnvelope.TagSize];
        if (ReadExact(_sourceStream, tag) < ZrusCryptoEnvelope.TagSize)
        {
            throw new ArchiveIntegrityException("Unexpected end of stream while reading chunk authentication tag.");
        }

        _currentPlaintextChunk = new byte[chunkLen];
        try
        {
            _aesGcm.Decrypt(nonce, ciphertext, tag, _currentPlaintextChunk);
        }
        catch (CryptographicException)
        {
            throw new ArchiveIntegrityException("Authentication failed for encrypted archive chunk (corrupted or tampered).");
        }

        _currentChunkOffset = 0;
        return true;
    }

    private async Task<bool> ReadNextChunkAsync(CancellationToken ct)
    {
        if (_isEndOfStream) return false;

        byte[] lenBytes = new byte[4];
        int read = await ReadExactAsync(_sourceStream, lenBytes, ct);
        if (read < 4)
        {
            _isEndOfStream = true;
            return false;
        }

        int chunkLen = BinaryPrimitives.ReadInt32LittleEndian(lenBytes);
        if (chunkLen == 0) // EOF marker
        {
            _isEndOfStream = true;
            return false;
        }

        if (chunkLen < 0 || chunkLen > ZrusCryptoEnvelope.ChunkPayloadSize * 2)
        {
            throw new ArchiveIntegrityException("Corrupt chunk length in encrypted archive.");
        }

        byte[] nonce = new byte[ZrusCryptoEnvelope.MasterNonceSize];
        if (await ReadExactAsync(_sourceStream, nonce, ct) < ZrusCryptoEnvelope.MasterNonceSize)
        {
            throw new ArchiveIntegrityException("Unexpected end of stream while reading chunk nonce.");
        }

        var ciphertext = new byte[chunkLen];
        if (await ReadExactAsync(_sourceStream, ciphertext, ct) < chunkLen)
        {
            throw new ArchiveIntegrityException("Unexpected end of stream while reading chunk ciphertext.");
        }

        byte[] tag = new byte[ZrusCryptoEnvelope.TagSize];
        if (await ReadExactAsync(_sourceStream, tag, ct) < ZrusCryptoEnvelope.TagSize)
        {
            throw new ArchiveIntegrityException("Unexpected end of stream while reading chunk authentication tag.");
        }

        _currentPlaintextChunk = new byte[chunkLen];
        try
        {
            _aesGcm.Decrypt(nonce, ciphertext, tag, _currentPlaintextChunk);
        }
        catch (CryptographicException)
        {
            throw new ArchiveIntegrityException("Authentication failed for encrypted archive chunk (corrupted or tampered).");
        }

        _currentChunkOffset = 0;
        return true;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        int totalRead = 0;
        while (count > 0)
        {
            if (_currentPlaintextChunk == null || _currentChunkOffset >= _currentPlaintextChunk.Length)
            {
                if (!ReadNextChunk()) break;
            }

            int available = _currentPlaintextChunk!.Length - _currentChunkOffset;
            int toCopy = Math.Min(count, available);
            Buffer.BlockCopy(_currentPlaintextChunk, _currentChunkOffset, buffer, offset, toCopy);

            _currentChunkOffset += toCopy;
            offset += toCopy;
            count -= toCopy;
            totalRead += toCopy;
        }

        return totalRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        int totalRead = 0;
        while (count > 0)
        {
            if (_currentPlaintextChunk == null || _currentChunkOffset >= _currentPlaintextChunk.Length)
            {
                if (!await ReadNextChunkAsync(cancellationToken)) break;
            }

            int available = _currentPlaintextChunk!.Length - _currentChunkOffset;
            int toCopy = Math.Min(count, available);
            Buffer.BlockCopy(_currentPlaintextChunk, _currentChunkOffset, buffer, offset, toCopy);

            _currentChunkOffset += toCopy;
            offset += toCopy;
            count -= toCopy;
            totalRead += toCopy;
        }

        return totalRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        int totalRead = 0;
        while (buffer.Length > 0)
        {
            if (_currentPlaintextChunk == null || _currentChunkOffset >= _currentPlaintextChunk.Length)
            {
                if (!await ReadNextChunkAsync(cancellationToken)) break;
            }

            int available = _currentPlaintextChunk!.Length - _currentChunkOffset;
            int toCopy = Math.Min(buffer.Length, available);
            _currentPlaintextChunk.AsSpan(_currentChunkOffset, toCopy).CopyTo(buffer.Span);

            _currentChunkOffset += toCopy;
            buffer = buffer[toCopy..];
            totalRead += toCopy;
        }

        return totalRead;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                _aesGcm.Dispose();
                CryptographicOperations.ZeroMemory(_key);

                if (!_leaveOpen)
                {
                    _sourceStream.Dispose();
                }
            }
            _isDisposed = true;
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            _aesGcm.Dispose();
            CryptographicOperations.ZeroMemory(_key);

            if (!_leaveOpen)
            {
                await _sourceStream.DisposeAsync();
            }
            _isDisposed = true;
        }
        await base.DisposeAsync();
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

namespace RusZip.Core.Engines;

/// <summary>
/// Composite virtual input stream that seamlessly stitches sequential split volume files into a unified readable stream.
/// Enforces continuity checks and throws <see cref="MissingVolumeException"/> if a volume is missing or unreachable.
/// </summary>
public sealed class MultiVolumeReadStream : Stream
{
    private const int BufferSize = 81920;

    private readonly IReadOnlyList<string> _volumeFilePaths;
    private readonly long[] _volumeLengths;
    private readonly long _totalLength;
    private int _currentIndex;
    private FileStream? _currentStream;
    private long _position;
    private bool _isDisposed;

    public IReadOnlyList<string> VolumeFilePaths => _volumeFilePaths;
    public int CurrentVolumeIndex => _currentIndex + 1;
    public int TotalVolumes => _volumeFilePaths.Count;

    public MultiVolumeReadStream(IReadOnlyList<string> volumeFilePaths)
    {
        if (volumeFilePaths is null or { Count: 0 })
        {
            throw new ArgumentException("At least one volume file path is required.", nameof(volumeFilePaths));
        }

        _volumeFilePaths = volumeFilePaths;
        _volumeLengths = new long[_volumeFilePaths.Count];

        for (int i = 0; i < _volumeFilePaths.Count; i++)
        {
            var path = _volumeFilePaths[i];
            if (!File.Exists(path))
            {
                throw new MissingVolumeException($"Volume part {i + 1} is missing: '{path}'", path, i + 1);
            }
            var fi = new FileInfo(path);
            _volumeLengths[i] = fi.Length;
            _totalLength += fi.Length;
        }

        _currentIndex = 0;
        OpenCurrentVolume();
    }

    private void OpenCurrentVolume()
    {
        _currentStream?.Dispose();
        var path = _volumeFilePaths[_currentIndex];
        if (!File.Exists(path))
        {
            throw new MissingVolumeException($"Volume part {_currentIndex + 1} is missing: '{path}'", path, _currentIndex + 1);
        }

        _currentStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        int totalRead = 0;
        while (count > 0 && _currentIndex < _volumeFilePaths.Count)
        {
            if (_currentStream == null)
            {
                OpenCurrentVolume();
            }

            int read = _currentStream!.Read(buffer, offset, count);
            if (read > 0)
            {
                totalRead += read;
                _position += read;
                offset += read;
                count -= read;
            }
            else
            {
                // Advance to next volume
                _currentStream.Dispose();
                _currentStream = null;
                _currentIndex++;
            }
        }

        return totalRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        int totalRead = 0;
        while (count > 0 && _currentIndex < _volumeFilePaths.Count)
        {
            if (_currentStream == null)
            {
                OpenCurrentVolume();
            }

            int read = await _currentStream!.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            if (read > 0)
            {
                totalRead += read;
                _position += read;
                offset += read;
                count -= read;
            }
            else
            {
                // Advance to next volume
                _currentStream.Dispose();
                _currentStream = null;
                _currentIndex++;
            }
        }

        return totalRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        int totalRead = 0;
        while (buffer.Length > 0 && _currentIndex < _volumeFilePaths.Count)
        {
            if (_currentStream == null)
            {
                OpenCurrentVolume();
            }

            int read = await _currentStream!.ReadAsync(buffer, cancellationToken);
            if (read > 0)
            {
                totalRead += read;
                _position += read;
                buffer = buffer[read..];
            }
            else
            {
                _currentStream.Dispose();
                _currentStream = null;
                _currentIndex++;
            }
        }

        return totalRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        long targetPos = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _totalLength + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (targetPos < 0 || targetPos > _totalLength)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Seek position is outside the bounds of the multi-volume stream.");
        }

        // Find which volume corresponds to targetPos
        long cumulative = 0;
        int targetIndex = 0;
        long offsetInVolume = 0;

        for (int i = 0; i < _volumeLengths.Length; i++)
        {
            if (targetPos < cumulative + _volumeLengths[i] || (i == _volumeLengths.Length - 1 && targetPos == _totalLength))
            {
                targetIndex = i;
                offsetInVolume = targetPos - cumulative;
                break;
            }
            cumulative += _volumeLengths[i];
        }

        if (targetIndex != _currentIndex || _currentStream == null)
        {
            _currentStream?.Dispose();
            _currentStream = null;
            _currentIndex = targetIndex;
            OpenCurrentVolume();
        }

        _currentStream!.Seek(offsetInVolume, SeekOrigin.Begin);
        _position = targetPos;
        return _position;
    }

    public override void Flush()
    {
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _totalLength;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

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
}

namespace RusZip.Core.Engines;

internal sealed class ProgressReportingStream : Stream
{
    private readonly Stream _innerStream;
    private readonly long _length;
    private readonly Action<int> _onBytesRead;

    public ProgressReportingStream(Stream innerStream, long length, Action<int> onBytesRead)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _length = length;
        _onBytesRead = onBytesRead ?? throw new ArgumentNullException(nameof(onBytesRead));
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _innerStream.ReadAsync(buffer, cancellationToken);
        if (read > 0) _onBytesRead(read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await _innerStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        if (read > 0) _onBytesRead(read);
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _innerStream.Read(buffer, offset, count);
        if (read > 0) _onBytesRead(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = _innerStream.Read(buffer);
        if (read > 0) _onBytesRead(read);
        return read;
    }

    public override void Flush() => _innerStream.Flush();
    public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _innerStream.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _innerStream.DisposeAsync();
        await base.DisposeAsync();
    }
}

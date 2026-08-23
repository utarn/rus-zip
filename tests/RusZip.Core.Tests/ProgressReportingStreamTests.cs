using RusZip.Core.Engines;
using Xunit;

namespace RusZip.Core.Tests;

public class ProgressReportingStreamTests
{
    [Fact]
    public async Task ProgressReportingStream_ReadAndReadAsync_ReportsBytesRead()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        using var memoryStream = new MemoryStream(data);

        int reportedBytes = 0;
        await using var stream = new ProgressReportingStream(memoryStream, data.Length, bytes => reportedBytes += bytes);

        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Equal(10, stream.Length);
        Assert.Equal(0, stream.Position);

        // Test Read(byte[], offset, count)
        var buf1 = new byte[2];
        var read1 = stream.Read(buf1, 0, 2);
        Assert.Equal(2, read1);
        Assert.Equal(2, reportedBytes);

        // Test Read(Span<byte>)
        Span<byte> buf2 = stackalloc byte[2];
        var read2 = stream.Read(buf2);
        Assert.Equal(2, read2);
        Assert.Equal(4, reportedBytes);

        // Test ReadAsync(byte[], offset, count, ct)
        var buf3 = new byte[2];
        var read3 = await stream.ReadAsync(buf3, 0, 2, CancellationToken.None);
        Assert.Equal(2, read3);
        Assert.Equal(6, reportedBytes);

        // Test ReadAsync(Memory<byte>, ct)
        var buf4 = new byte[4];
        var read4 = await stream.ReadAsync(buf4.AsMemory(), CancellationToken.None);
        Assert.Equal(4, read4);
        Assert.Equal(10, reportedBytes);

        // Test Seek, Position, Flush
        stream.Seek(0, SeekOrigin.Begin);
        Assert.Equal(0, stream.Position);
        stream.Position = 5;
        Assert.Equal(5, stream.Position);
        stream.Flush();

        // Unsupported operations
        Assert.Throws<NotSupportedException>(() => stream.SetLength(20));
        Assert.Throws<NotSupportedException>(() => stream.Write([1, 2], 0, 2));
    }

    [Fact]
    public void ProgressReportingStream_NullArguments_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ProgressReportingStream(null!, 10, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new ProgressReportingStream(new MemoryStream(), 10, null!));
    }
}

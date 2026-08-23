using RusZip.Core.Engines;
using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

public class ArchiveRequestsAndExceptionsTests
{
    [Fact]
    public void ArchiveCompressionRequest_ConstructorsAndProperties()
    {
        var req1 = new ArchiveCompressionRequest("/path/to/file.txt", "/path/to/dest.zip");
        Assert.Equal("/path/to/file.txt", req1.SourcePath);
        Assert.Single(req1.SourcePaths);
        Assert.Equal(9, req1.CompressionLevel);
        Assert.Empty(req1.ExcludedPaths);

        var req2 = new ArchiveCompressionRequest(["/p1", "/p2"], "/dest.zip", 5, "/base", ["*.tmp"]);
        Assert.Equal("/p1", req2.SourcePath);
        Assert.Equal(2, req2.SourcePaths.Count);
        Assert.Equal(5, req2.CompressionLevel);
        Assert.Equal("/base", req2.BaseDirectory);
        Assert.Single(req2.ExcludedPaths);

        var reqEmpty = new ArchiveCompressionRequest([], "/dest.zip");
        Assert.Equal(string.Empty, reqEmpty.SourcePath);
    }

    [Fact]
    public void ArchiveAppendRequest_ConstructorsAndProperties()
    {
        var req1 = new ArchiveAppendRequest("/dest.zip", "/source.txt");
        Assert.Equal("/dest.zip", req1.ArchivePath);
        Assert.Single(req1.SourcePaths);
        Assert.Equal("/source.txt", req1.SourcePaths[0]);
        Assert.Equal(9, req1.CompressionLevel);
        Assert.False(req1.UpdateOnly);

        var req2 = new ArchiveAppendRequest("/dest.zip", ["/s1", "/s2"], 6, true, "/base");
        Assert.Equal(2, req2.SourcePaths.Count);
        Assert.Equal(6, req2.CompressionLevel);
        Assert.True(req2.UpdateOnly);
        Assert.Equal("/base", req2.BaseDirectory);
    }

    [Fact]
    public void ArchiveDeleteRequest_ConstructorsAndProperties()
    {
        var req1 = new ArchiveDeleteRequest("/dest.zip", "entry.txt");
        Assert.Equal("/dest.zip", req1.ArchivePath);
        Assert.Single(req1.EntryPaths);
        Assert.Equal("entry.txt", req1.EntryPaths[0]);

        var req2 = new ArchiveDeleteRequest("/dest.zip", ["e1.txt", "e2.txt"], 5);
        Assert.Equal(2, req2.EntryPaths.Count);
        Assert.Equal(5, req2.CompressionLevel);
    }

    [Fact]
    public void ExtractionLimitExceededException_Constructors()
    {
        var ex1 = new ExtractionLimitExceededException("Limit exceeded");
        Assert.Equal("Limit exceeded", ex1.Message);

        var inner = new InvalidOperationException("Inner");
        var ex2 = new ExtractionLimitExceededException("Wrapped", inner);
        Assert.Equal("Wrapped", ex2.Message);
        Assert.Same(inner, ex2.InnerException);
    }

    [Fact]
    public void ArchiveDeleteResult_Properties()
    {
        var result = new ArchiveDeleteResult(true, "/path/archive.zrus", 3, 10, 5000, 2500, 150);
        Assert.True(result.Success);
        Assert.Equal("/path/archive.zrus", result.ArchivePath);
        Assert.Equal(3, result.DeletedEntriesCount);
        Assert.Equal(10, result.RetainedEntriesCount);
        Assert.Equal(5000, result.UncompressedBytes);
        Assert.Equal(2500, result.CompressedBytes);
        Assert.Equal(150, result.ElapsedMilliseconds);
    }
}

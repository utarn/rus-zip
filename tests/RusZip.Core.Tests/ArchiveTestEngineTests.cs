using System.IO.Compression;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

public sealed class ArchiveTestEngineTests : IDisposable
{
    private readonly string _tempDirectory;

    public ArchiveTestEngineTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ruszip-test-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { /* Ignored */ }
        }
    }

    [Fact]
    public async Task TestArchiveAsync_ValidZrusArchive_ReturnsSuccessAndZeroErrors()
    {
        var sampleDir = Path.Combine(_tempDirectory, "sample");
        Directory.CreateDirectory(sampleDir);
        await File.WriteAllTextAsync(Path.Combine(sampleDir, "file1.txt"), "Hello World 1");
        await File.WriteAllTextAsync(Path.Combine(sampleDir, "file2.txt"), "Hello World 2");

        var zrusPath = Path.Combine(_tempDirectory, "test.zrus");
        var engine = new ZstdTarArchiveEngine();
        await engine.CompressAsync(new ArchiveCompressionRequest([sampleDir], zrusPath, 3));

        var result = await engine.TestArchiveAsync(zrusPath);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.True(result.TotalEntries >= 2);
        Assert.True(result.UncompressedBytes > 0);
        Assert.True(result.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task TestArchiveAsync_ValidZipArchive_ReturnsSuccess()
    {
        var sampleDir = Path.Combine(_tempDirectory, "sample_zip");
        Directory.CreateDirectory(sampleDir);
        await File.WriteAllTextAsync(Path.Combine(sampleDir, "data.txt"), "Zip compression sample data");

        var zipPath = Path.Combine(_tempDirectory, "test.zip");
        var engine = new SharpCompressArchiveEngine();
        await engine.CompressAsync(new ArchiveCompressionRequest([sampleDir], zipPath, 5));

        var result = await engine.TestArchiveAsync(zipPath);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.True(result.TotalEntries >= 1);
        Assert.True(result.UncompressedBytes > 0);
    }

    [Fact]
    public async Task TestArchiveAsync_CorruptedZrusArchive_ReturnsFailureWithDiagnostics()
    {
        var sampleDir = Path.Combine(_tempDirectory, "sample_corrupt");
        Directory.CreateDirectory(sampleDir);
        await File.WriteAllTextAsync(Path.Combine(sampleDir, "important.txt"), new string('A', 5000));

        var zrusPath = Path.Combine(_tempDirectory, "corrupt.zrus");
        var engine = new ZstdTarArchiveEngine();
        await engine.CompressAsync(new ArchiveCompressionRequest([sampleDir], zrusPath, 3));

        // Corrupt archive bytes in the middle of the payload
        var bytes = await File.ReadAllBytesAsync(zrusPath);
        for (int i = bytes.Length / 2; i < Math.Min(bytes.Length, bytes.Length / 2 + 30); i++)
        {
            bytes[i] = 0xFF;
        }
        await File.WriteAllBytesAsync(zrusPath, bytes);

        var result = await engine.TestArchiveAsync(zrusPath);

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task TestArchiveAsync_CorruptedZipArchive_ReturnsFailureWithDiagnostics()
    {
        var zipPath = Path.Combine(_tempDirectory, "corrupt.zip");
        await File.WriteAllBytesAsync(zipPath, [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0xFF, 0xFF]);

        var engine = new SharpCompressArchiveEngine();
        var result = await engine.TestArchiveAsync(zipPath);

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task TestArchiveAsync_UnifiedArchiveEngine_RoutesCorrectly()
    {
        var sampleDir = Path.Combine(_tempDirectory, "sample_unified");
        Directory.CreateDirectory(sampleDir);
        await File.WriteAllTextAsync(Path.Combine(sampleDir, "test.txt"), "unified test content");

        var zrusPath = Path.Combine(_tempDirectory, "unified.zrus");
        var zipPath = Path.Combine(_tempDirectory, "unified.zip");

        var unifiedEngine = new UnifiedArchiveEngine();
        await unifiedEngine.CompressAsync(new ArchiveCompressionRequest([sampleDir], zrusPath, 3));
        await unifiedEngine.CompressAsync(new ArchiveCompressionRequest([sampleDir], zipPath, 3));

        var zrusResult = await unifiedEngine.TestArchiveAsync(zrusPath);
        var zipResult = await unifiedEngine.TestArchiveAsync(zipPath);

        Assert.True(zrusResult.IsSuccess);
        Assert.True(zipResult.IsSuccess);
    }
}

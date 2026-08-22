using RusZip.Core.Engines;
using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

public class UnifiedArchiveEngineTests : IDisposable
{
    private readonly string _testDir;
    private readonly UnifiedArchiveEngine _engine;

    public UnifiedArchiveEngineTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ruszip_unified_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _engine = new UnifiedArchiveEngine();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { /* Ignore */ }
        }
    }

    [Fact]
    public async Task UnifiedEngine_RoutesZrusAndZipCorrectly()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "data");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "info.json"), "{\"app\":\"rus-zip\"}");

        var zrusPath = Path.Combine(_testDir, "test.zrus");
        var zipPath = Path.Combine(_testDir, "test.zip");

        // Act - Compress .zrus
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zrusPath, 9));
        Assert.True(File.Exists(zrusPath));

        // Act - Compress .zip
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));
        Assert.True(File.Exists(zipPath));

        // List both
        var zrusEntries = await _engine.ListEntriesAsync(zrusPath);
        var zipEntries = await _engine.ListEntriesAsync(zipPath);

        Assert.Contains(zrusEntries, e => e.RelativePath == "info.json");
        Assert.Contains(zipEntries, e => e.RelativePath == "info.json");

        // Extract both
        var zrusExtract = Path.Combine(_testDir, "zrus_out");
        var zipExtract = Path.Combine(_testDir, "zip_out");

        await _engine.ExtractAsync(new ArchiveExtractionRequest(zrusPath, zrusExtract));
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, zipExtract));

        Assert.True(File.Exists(Path.Combine(zrusExtract, "info.json")));
        Assert.True(File.Exists(Path.Combine(zipExtract, "info.json")));
    }
}

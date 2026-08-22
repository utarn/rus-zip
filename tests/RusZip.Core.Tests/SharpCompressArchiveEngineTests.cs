using RusZip.Core.Engines;
using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

public class SharpCompressArchiveEngineTests : IDisposable
{
    private readonly string _testDir;
    private readonly SharpCompressArchiveEngine _engine;

    public SharpCompressArchiveEngineTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ruszip_sharpcompress_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _engine = new SharpCompressArchiveEngine();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { /* Ignore */ }
        }
    }

    [Fact]
    public async Task Zip_CompressAndExtract_PreservesFiles()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "zip_source");
        Directory.CreateDirectory(sourceDir);
        var file1 = Path.Combine(sourceDir, "doc.txt");
        await File.WriteAllTextAsync(file1, "Zip compression test content.");

        var zipPath = Path.Combine(_testDir, "test.zip");
        var extractDir = Path.Combine(_testDir, "zip_extracted");

        // Act - Compress
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));
        Assert.True(File.Exists(zipPath));

        // Act - List
        var entries = await _engine.ListEntriesAsync(zipPath);
        Assert.Contains(entries, e => e.RelativePath == "doc.txt");

        // Act - Extract
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        // Assert
        var extractedDoc = Path.Combine(extractDir, "doc.txt");
        Assert.True(File.Exists(extractedDoc));
        Assert.Equal("Zip compression test content.", await File.ReadAllTextAsync(extractedDoc));
    }
}

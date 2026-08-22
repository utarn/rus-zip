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

    [Theory]
    [InlineData("output.rar")]
    [InlineData("output.7z")]
    [InlineData("output.gz")]
    [InlineData("output.tar.gz")]
    [InlineData("output.tgz")]
    public async Task UnifiedEngine_Compress_UnsupportedFormat_ThrowsNotSupportedException(string unsupportedArchiveName)
    {
        var dummyFile = Path.Combine(_testDir, "dummy.txt");
        await File.WriteAllTextAsync(dummyFile, "dummy");

        var destination = Path.Combine(_testDir, unsupportedArchiveName);
        var req = new ArchiveCompressionRequest(dummyFile, destination, 9);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => _engine.CompressAsync(req));
        Assert.Contains("Supported creation formats: .zrus, .zip", ex.Message);
    }

    [Fact]
    public async Task UnifiedEngine_ExtractAndList_RoutesAllDecompressFormats()
    {
        // 1. .7z
        var sevenZipPath = Path.Combine(_testDir, "test.7z");
        TestArchiveFixtures.CreateSevenZipArchive(sevenZipPath, "seven.txt", "7z via unified engine");
        var sevenEntries = await _engine.ListEntriesAsync(sevenZipPath);
        Assert.Contains(sevenEntries, e => e.RelativePath == "seven.txt");
        var sevenExtract = Path.Combine(_testDir, "seven_out");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(sevenZipPath, sevenExtract));
        Assert.True(File.Exists(Path.Combine(sevenExtract, "seven.txt")));

        // 2. .gz
        var gzPath = Path.Combine(_testDir, "file.txt.gz");
        await TestArchiveFixtures.CreateGzArchiveAsync(gzPath, "gz via unified engine");
        var gzEntries = await _engine.ListEntriesAsync(gzPath);
        Assert.Contains(gzEntries, e => e.RelativePath == "file.txt");
        var gzExtract = Path.Combine(_testDir, "gz_out");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(gzPath, gzExtract));
        Assert.True(File.Exists(Path.Combine(gzExtract, "file.txt")));

        // 3. .tar.gz
        var tarGzPath = Path.Combine(_testDir, "package.tar.gz");
        await TestArchiveFixtures.CreateTarGzArchiveAsync(tarGzPath, new Dictionary<string, string> { ["targz.txt"] = "targz payload" });
        var tarGzEntries = await _engine.ListEntriesAsync(tarGzPath);
        Assert.Contains(tarGzEntries, e => e.RelativePath == "targz.txt");
        var tarGzExtract = Path.Combine(_testDir, "targz_out");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(tarGzPath, tarGzExtract));
        Assert.True(File.Exists(Path.Combine(tarGzExtract, "targz.txt")));

        // 4. .tgz
        var tgzPath = Path.Combine(_testDir, "bundle.tgz");
        await TestArchiveFixtures.CreateTarGzArchiveAsync(tgzPath, new Dictionary<string, string> { ["tgz.txt"] = "tgz payload" });
        var tgzEntries = await _engine.ListEntriesAsync(tgzPath);
        Assert.Contains(tgzEntries, e => e.RelativePath == "tgz.txt");
        var tgzExtract = Path.Combine(_testDir, "tgz_out");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(tgzPath, tgzExtract));
        Assert.True(File.Exists(Path.Combine(tgzExtract, "tgz.txt")));

        // 5. .rar (RAR4)
        var rar4Path = Path.Combine(_testDir, "sample4.rar");
        TestArchiveFixtures.CreateRar4Archive(rar4Path, "rar4.txt", "rar4 payload");
        var rar4Entries = await _engine.ListEntriesAsync(rar4Path);
        Assert.Contains(rar4Entries, e => e.RelativePath == "rar4.txt");
        var rar4Extract = Path.Combine(_testDir, "rar4_out");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(rar4Path, rar4Extract));
        Assert.True(File.Exists(Path.Combine(rar4Extract, "rar4.txt")));

        // 6. .rar (RAR5)
        var rar5Path = Path.Combine(_testDir, "sample5.rar");
        TestArchiveFixtures.CreateRar5Archive(rar5Path, "rar5.txt", "rar5 payload");
        var rar5Entries = await _engine.ListEntriesAsync(rar5Path);
        Assert.Contains(rar5Entries, e => e.RelativePath == "rar5.txt");
        var rar5Extract = Path.Combine(_testDir, "rar5_out");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(rar5Path, rar5Extract));
        Assert.True(File.Exists(Path.Combine(rar5Extract, "rar5.txt")));
    }

    [Fact]
    public void ArchiveFormatRegistry_UnknownExtension_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => ArchiveFormatRegistry.Detect("file.unknown_extension"));
    }
}

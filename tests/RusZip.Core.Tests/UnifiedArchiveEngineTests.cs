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
    [InlineData("test.tar.zstd")]
    [InlineData("test.tzstd")]
    public async Task UnifiedEngine_RoutesTarZstdAliasesCorrectly(string archiveFileName)
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "data_" + Path.GetExtension(archiveFileName).TrimStart('.'));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "info.json"), "{\"app\":\"rus-zip-alias\"}");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "extra.txt"), "extra payload");

        var archivePath = Path.Combine(_testDir, archiveFileName);

        // Act - Compress
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 9));
        Assert.True(File.Exists(archivePath));

        // Act - List
        var entries = await _engine.ListEntriesAsync(archivePath);
        Assert.Contains(entries, e => e.RelativePath == "info.json");
        Assert.Contains(entries, e => e.RelativePath == "extra.txt");

        // Act - Append
        var appendFile = Path.Combine(_testDir, "appended_" + Path.GetExtension(archiveFileName).TrimStart('.') + ".txt");
        await File.WriteAllTextAsync(appendFile, "appended content");
        var appendResult = await _engine.AppendAsync(new ArchiveAppendRequest(archivePath, [appendFile], 9));
        Assert.True(appendResult.Success);
        Assert.Equal(3, appendResult.TotalFiles);

        // Act - List after append
        var entriesAfterAppend = await _engine.ListEntriesAsync(archivePath);
        Assert.Contains(entriesAfterAppend, e => e.RelativePath == Path.GetFileName(appendFile));

        // Act - Extract
        var extractDir = Path.Combine(_testDir, "extract_" + Path.GetExtension(archiveFileName).TrimStart('.'));
        var extractResult = await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));
        Assert.Equal(3, extractResult.FilesExtracted);
        Assert.True(extractResult.BytesExtracted > 0);
        Assert.True(File.Exists(Path.Combine(extractDir, "info.json")));
        Assert.True(File.Exists(Path.Combine(extractDir, "extra.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, Path.GetFileName(appendFile))));
        Assert.Equal("{\"app\":\"rus-zip-alias\"}", await File.ReadAllTextAsync(Path.Combine(extractDir, "info.json")));
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
        Assert.Contains("Supported creation formats: .zrus, .zip, .zst", ex.Message);
    }

    [Fact]
    public async Task UnifiedEngine_RoutesZstCorrectly()
    {
        var sourceFile = Path.Combine(_testDir, "single.txt");
        await File.WriteAllTextAsync(sourceFile, "Hello Zstandard stream!");

        var zstPath = Path.Combine(_testDir, "single.txt.zst");

        // Compress
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceFile, zstPath, 9));
        Assert.True(File.Exists(zstPath));

        // List
        var entries = await _engine.ListEntriesAsync(zstPath);
        var entry = Assert.Single(entries);
        Assert.Equal("single.txt", entry.RelativePath);
        Assert.False(entry.IsDirectory);
        Assert.Equal("Hello Zstandard stream!".Length, entry.UncompressedSize);

        // Extract
        var extractDir = Path.Combine(_testDir, "zst_extract_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(zstPath, extractDir));
        Assert.Equal(1, result.FilesExtracted);
        var extractedFile = Path.Combine(extractDir, "single.txt");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal("Hello Zstandard stream!", await File.ReadAllTextAsync(extractedFile));
    }

    [Fact]
    public async Task UnifiedEngine_Append_Zst_ThrowsNotSupportedException()
    {
        var sourceFile = Path.Combine(_testDir, "append_base.txt");
        await File.WriteAllTextAsync(sourceFile, "Initial stream");

        var zstPath = Path.Combine(_testDir, "append_base.txt.zst");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceFile, zstPath, 9));

        var appendFile = Path.Combine(_testDir, "extra.txt");
        await File.WriteAllTextAsync(appendFile, "Extra");

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            _engine.AppendAsync(new ArchiveAppendRequest(zstPath, [appendFile], 9)));
        Assert.Contains("Appending is not supported for single-file streams", ex.Message);
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
    public async Task UnifiedEngine_AppendAsync_RoutesZrusAndZipCorrectly()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "append_data");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "initial.txt"), "Initial content");

        var zrusPath = Path.Combine(_testDir, "unified_append.zrus");
        var zipPath = Path.Combine(_testDir, "unified_append.zip");

        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zrusPath, 9));
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));

        var appendFile = Path.Combine(_testDir, "new_file.txt");
        await File.WriteAllTextAsync(appendFile, "New content to append");

        // Act - Append to .zrus
        var zrusAppendResult = await _engine.AppendAsync(new ArchiveAppendRequest(zrusPath, [appendFile], 9));
        Assert.True(zrusAppendResult.Success);
        Assert.Equal("zrus", zrusAppendResult.Format);
        Assert.Equal(2, zrusAppendResult.TotalFiles);

        // Act - Append to .zip
        var zipAppendResult = await _engine.AppendAsync(new ArchiveAppendRequest(zipPath, [appendFile], 9));
        Assert.True(zipAppendResult.Success);
        Assert.Equal("zip", zipAppendResult.Format);
        Assert.Equal(2, zipAppendResult.TotalFiles);

        // Verify entries
        var zrusEntries = await _engine.ListEntriesAsync(zrusPath);
        var zipEntries = await _engine.ListEntriesAsync(zipPath);

        Assert.Contains(zrusEntries, e => e.RelativePath == "new_file.txt");
        Assert.Contains(zipEntries, e => e.RelativePath == "new_file.txt");
    }

    [Theory]
    [InlineData("output.rar")]
    [InlineData("output.7z")]
    [InlineData("output.gz")]
    [InlineData("output.tar.gz")]
    [InlineData("output.tgz")]
    public async Task UnifiedEngine_Append_UnsupportedFormat_ThrowsNotSupportedException(string unsupportedArchiveName)
    {
        var dummyFile = Path.Combine(_testDir, "dummy_append.txt");
        await File.WriteAllTextAsync(dummyFile, "dummy");

        var destination = Path.Combine(_testDir, unsupportedArchiveName);
        await File.WriteAllTextAsync(destination, "dummy archive content");

        var req = new ArchiveAppendRequest(destination, [dummyFile], 9);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => _engine.AppendAsync(req));
        Assert.Contains("Appending to", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(-1)]
    [InlineData(100)]
    public async Task UnifiedEngine_Append_Zrus_InvalidCompressionLevel_ThrowsArgumentOutOfRangeException(int invalidLevel)
    {
        var sourceDir = Path.Combine(_testDir, $"zrus_lvl_{invalidLevel}_dir");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "initial.txt"), "Initial");

        var zrusPath = Path.Combine(_testDir, $"lvl_test_{invalidLevel}.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zrusPath, 9));

        var appendFile = Path.Combine(_testDir, $"append_{invalidLevel}.txt");
        await File.WriteAllTextAsync(appendFile, "Append text");

        var req = new ArchiveAppendRequest(zrusPath, [appendFile], invalidLevel);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _engine.AppendAsync(req));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(22)]
    public async Task UnifiedEngine_Append_Zip_InvalidCompressionLevel_ThrowsArgumentOutOfRangeException(int invalidLevel)
    {
        var sourceDir = Path.Combine(_testDir, $"zip_lvl_{invalidLevel}_dir");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "initial.txt"), "Initial");

        var zipPath = Path.Combine(_testDir, $"lvl_test_{invalidLevel}.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));

        var appendFile = Path.Combine(_testDir, $"append_zip_{invalidLevel}.txt");
        await File.WriteAllTextAsync(appendFile, "Append text");

        var req = new ArchiveAppendRequest(zipPath, [appendFile], invalidLevel);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _engine.AppendAsync(req));
    }

    [Fact]
    public async Task UnifiedEngine_DeleteEntriesAsync_RoutesZrusAndZipCorrectly()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "unified_del_data");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "keep.txt"), "Keep this file");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "remove.txt"), "Remove this file");

        var zrusPath = Path.Combine(_testDir, "unified_del.zrus");
        var zipPath = Path.Combine(_testDir, "unified_del.zip");

        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zrusPath, 9));
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));

        // Act - Delete from .zrus
        var zrusDelResult = await _engine.DeleteEntriesAsync(new ArchiveDeleteRequest(zrusPath, ["remove.txt"], 9));
        Assert.True(zrusDelResult.Success);
        Assert.Equal(1, zrusDelResult.DeletedEntriesCount);
        Assert.Equal(1, zrusDelResult.RetainedEntriesCount);

        // Act - Delete from .zip
        var zipDelResult = await _engine.DeleteEntriesAsync(new ArchiveDeleteRequest(zipPath, ["remove.txt"], 9));
        Assert.True(zipDelResult.Success);
        Assert.Equal(1, zipDelResult.DeletedEntriesCount);
        Assert.Equal(1, zipDelResult.RetainedEntriesCount);

        // Verify entries
        var zrusEntries = await _engine.ListEntriesAsync(zrusPath);
        var zipEntries = await _engine.ListEntriesAsync(zipPath);

        Assert.Contains(zrusEntries, e => e.RelativePath == "keep.txt");
        Assert.DoesNotContain(zrusEntries, e => e.RelativePath == "remove.txt");

        Assert.Contains(zipEntries, e => e.RelativePath == "keep.txt");
        Assert.DoesNotContain(zipEntries, e => e.RelativePath == "remove.txt");
    }

    [Theory]
    [InlineData("test_del.tar.zstd")]
    [InlineData("test_del.tzstd")]
    public async Task UnifiedEngine_DeleteEntriesAsync_RoutesTarZstdAliasesCorrectly(string archiveFileName)
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "data_del_" + Path.GetExtension(archiveFileName).TrimStart('.'));
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "keep.txt"), "Keep payload");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "delete.txt"), "Delete payload");

        var archivePath = Path.Combine(_testDir, archiveFileName);
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 9));

        // Act
        var delResult = await _engine.DeleteEntriesAsync(new ArchiveDeleteRequest(archivePath, ["delete.txt"], 9));
        Assert.True(delResult.Success);
        Assert.Equal(1, delResult.DeletedEntriesCount);
        Assert.Equal(1, delResult.RetainedEntriesCount);

        // Verify
        var entries = await _engine.ListEntriesAsync(archivePath);
        Assert.Contains(entries, e => e.RelativePath == "keep.txt");
        Assert.DoesNotContain(entries, e => e.RelativePath == "delete.txt");

        var extractDir = Path.Combine(_testDir, "extract_del_" + Path.GetExtension(archiveFileName).TrimStart('.'));
        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));
        Assert.True(File.Exists(Path.Combine(extractDir, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(extractDir, "delete.txt")));
    }

    [Theory]
    [InlineData("output_del.rar")]
    [InlineData("output_del.7z")]
    [InlineData("output_del.gz")]
    [InlineData("output_del.tar.gz")]
    [InlineData("output_del.tgz")]
    public async Task UnifiedEngine_Delete_ReadOnlyFormat_ThrowsNotSupportedException(string unsupportedArchiveName)
    {
        var destination = Path.Combine(_testDir, unsupportedArchiveName);
        await File.WriteAllTextAsync(destination, "dummy archive content");

        var req = new ArchiveDeleteRequest(destination, ["file.txt"], 9);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => _engine.DeleteEntriesAsync(req));
        Assert.Contains("Deleting entries from", ex.Message);
    }

    [Fact]
    public async Task UnifiedEngine_Delete_Zst_ThrowsNotSupportedException()
    {
        var sourceFile = Path.Combine(_testDir, "del_zst_base.txt");
        await File.WriteAllTextAsync(sourceFile, "Stream text");

        var zstPath = Path.Combine(_testDir, "del_zst_base.txt.zst");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceFile, zstPath, 9));

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            _engine.DeleteEntriesAsync(new ArchiveDeleteRequest(zstPath, ["del_zst_base.txt"], 9)));
        Assert.Contains("Deleting entries is not supported for single-file streams", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(-1)]
    public async Task UnifiedEngine_Delete_Zrus_InvalidCompressionLevel_ThrowsArgumentOutOfRangeException(int invalidLevel)
    {
        var sourceDir = Path.Combine(_testDir, $"zrus_del_lvl_{invalidLevel}_dir");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "initial.txt"), "Initial");

        var zrusPath = Path.Combine(_testDir, $"lvl_del_test_{invalidLevel}.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zrusPath, 9));

        var req = new ArchiveDeleteRequest(zrusPath, ["initial.txt"], invalidLevel);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _engine.DeleteEntriesAsync(req));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public async Task UnifiedEngine_Delete_Zip_InvalidCompressionLevel_ThrowsArgumentOutOfRangeException(int invalidLevel)
    {
        var sourceDir = Path.Combine(_testDir, $"zip_del_lvl_{invalidLevel}_dir");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "initial.txt"), "Initial");

        var zipPath = Path.Combine(_testDir, $"lvl_del_test_{invalidLevel}.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));

        var req = new ArchiveDeleteRequest(zipPath, ["initial.txt"], invalidLevel);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _engine.DeleteEntriesAsync(req));
    }

    [Fact]
    public void ArchiveFormatRegistry_UnknownExtension_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => ArchiveFormatRegistry.Detect("file.unknown_extension"));
    }
}

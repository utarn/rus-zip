using System.Security;
using System.Text;
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

    #region Zip Compression & Extraction Tests

    [Fact]
    public async Task Zip_CompressAndExtract_PreservesDirectoryStructureAndFiles()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "zip_source");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(Path.Combine(sourceDir, "subfolder"));

        var file1 = Path.Combine(sourceDir, "doc.txt");
        var file2 = Path.Combine(sourceDir, "subfolder", "nested.txt");
        await File.WriteAllTextAsync(file1, "Zip compression test content.");
        await File.WriteAllTextAsync(file2, "Nested zip content.");

        var zipPath = Path.Combine(_testDir, "test.zip");
        var extractDir = Path.Combine(_testDir, "zip_extracted");

        var progressReports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(progressReports.Add);

        // Act - Compress
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9), progress);
        Assert.True(File.Exists(zipPath));

        // Act - List
        var entries = await _engine.ListEntriesAsync(zipPath);
        Assert.Contains(entries, e => e.RelativePath == "doc.txt" && !e.IsDirectory);
        Assert.Contains(entries, e => e.RelativePath.Contains("nested.txt") && !e.IsDirectory);

        // Act - Extract
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir), progress);

        // Assert
        var extractedDoc = Path.Combine(extractDir, "doc.txt");
        var extractedNested = Path.Combine(extractDir, "subfolder", "nested.txt");
        Assert.True(File.Exists(extractedDoc));
        Assert.True(File.Exists(extractedNested));
        Assert.Equal("Zip compression test content.", await File.ReadAllTextAsync(extractedDoc));
        Assert.Equal("Nested zip content.", await File.ReadAllTextAsync(extractedNested));
        Assert.NotEmpty(progressReports);
    }

    [Fact]
    public async Task Zip_Compress_Level0_Store_CreatesValidArchive()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "zip_store_src");
        Directory.CreateDirectory(sourceDir);
        var file = Path.Combine(sourceDir, "uncompressed.txt");
        await File.WriteAllTextAsync(file, "Store level 0 test content that should remain uncompressed.");

        var zipPath = Path.Combine(_testDir, "store.zip");
        var extractDir = Path.Combine(_testDir, "store_extracted");

        // Act - Compress with Level 0 (Store)
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 0));
        Assert.True(File.Exists(zipPath));

        // Act - Extract
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        // Assert
        var extractedFile = Path.Combine(extractDir, "uncompressed.txt");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal("Store level 0 test content that should remain uncompressed.", await File.ReadAllTextAsync(extractedFile));
    }

    [Fact]
    public async Task Zip_Compress_NegativeLevel_ThrowsArgumentOutOfRangeException()
    {
        var dummyFile = Path.Combine(_testDir, "dummy.txt");
        await File.WriteAllTextAsync(dummyFile, "dummy");
        var zipPath = Path.Combine(_testDir, "invalid.zip");

        var req = new ArchiveCompressionRequest(dummyFile, zipPath, -1);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _engine.CompressAsync(req));
    }

    [Fact]
    public async Task Zip_Compress_NonExistentSource_ThrowsFileNotFoundException()
    {
        var nonExistent = Path.Combine(_testDir, "missing_source");
        var zipPath = Path.Combine(_testDir, "missing.zip");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _engine.CompressAsync(new ArchiveCompressionRequest(nonExistent, zipPath, 9)));
    }

    [Fact]
    public async Task Zip_Extract_NonExistentArchive_ThrowsFileNotFoundException()
    {
        var missingZip = Path.Combine(_testDir, "does_not_exist.zip");
        var extractDir = Path.Combine(_testDir, "out");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(missingZip, extractDir)));
    }

    [Fact]
    public async Task Zip_CompressAndExtract_SingleFile_PreservesContentAndTimestamp()
    {
        // Arrange
        var singleFile = Path.Combine(_testDir, "single.txt");
        var content = "Single file zip test.";
        await File.WriteAllTextAsync(singleFile, content);

        var expectedTime = new DateTime(2024, 8, 12, 15, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(singleFile, expectedTime);

        var zipPath = Path.Combine(_testDir, "single.zip");
        var extractDir = Path.Combine(_testDir, "single_extracted");

        // Act
        await _engine.CompressAsync(new ArchiveCompressionRequest(singleFile, zipPath, 9));
        var entries = await _engine.ListEntriesAsync(zipPath);
        Assert.Single(entries);
        Assert.Equal("single.txt", entries[0].RelativePath);

        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        // Assert
        var extracted = Path.Combine(extractDir, "single.txt");
        Assert.True(File.Exists(extracted));
        Assert.Equal(content, await File.ReadAllTextAsync(extracted));

        var extractedTime = File.GetLastWriteTimeUtc(extracted);
        Assert.True(Math.Abs((extractedTime - expectedTime).TotalSeconds) < 2);
    }

    [Fact]
    public async Task Zip_CompressAndExtract_EmptyDirectories_Preserved()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "empty_dir_src");
        Directory.CreateDirectory(Path.Combine(sourceDir, "folder1", "nested_empty"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "folder1", "file.txt"), "hello");

        var zipPath = Path.Combine(_testDir, "empty_dirs.zip");
        var extractDir = Path.Combine(_testDir, "empty_dirs_extracted");

        // Act
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        // Assert
        Assert.True(Directory.Exists(Path.Combine(extractDir, "folder1", "nested_empty")));
        Assert.True(File.Exists(Path.Combine(extractDir, "folder1", "file.txt")));
    }

    [Fact]
    public async Task Zip_CompressAndExtract_LargeFile_ChunkedProgressAndZip64()
    {
        // Arrange - 2 MB payload to verify chunked reading
        var sourceDir = Path.Combine(_testDir, "large_src");
        Directory.CreateDirectory(sourceDir);
        var largeFile = Path.Combine(sourceDir, "large.bin");
        var payload = new byte[2 * 1024 * 1024]; // 2 MB
        Random.Shared.NextBytes(payload);
        await File.WriteAllBytesAsync(largeFile, payload);

        var zipPath = Path.Combine(_testDir, "large.zip");
        var extractDir = Path.Combine(_testDir, "large_extracted");

        var compressProgress = new List<ProgressReport>();
        var extractProgress = new List<ProgressReport>();

        // Act - Compress
        await _engine.CompressAsync(
            new ArchiveCompressionRequest(sourceDir, zipPath, 9),
            new Progress<ProgressReport>(compressProgress.Add));

        // Act - Extract
        await _engine.ExtractAsync(
            new ArchiveExtractionRequest(zipPath, extractDir),
            new Progress<ProgressReport>(extractProgress.Add));

        // Assert
        var extractedFile = Path.Combine(extractDir, "large.bin");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal(payload, await File.ReadAllBytesAsync(extractedFile));

        // Chunked 80KB stream reading yields multiple progress reports
        Assert.True(compressProgress.Count > 1, $"Expected multiple progress reports during compression, got {compressProgress.Count}");
        Assert.True(extractProgress.Count > 1, $"Expected multiple progress reports during extraction, got {extractProgress.Count}");
    }

    [Fact]
    public async Task Zip_Extract_OverwriteFalse_WhenFileAlreadyExists_ThrowsIOException()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "overwrite_src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "test.txt"), "new content");

        var zipPath = Path.Combine(_testDir, "overwrite.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));

        var extractDir = Path.Combine(_testDir, "overwrite_dst");
        Directory.CreateDirectory(extractDir);
        await File.WriteAllTextAsync(Path.Combine(extractDir, "test.txt"), "existing content");

        // Act & Assert
        var req = new ArchiveExtractionRequest(zipPath, extractDir, Overwrite: false);
        await Assert.ThrowsAsync<IOException>(() => _engine.ExtractAsync(req));
    }

    [Fact]
    public async Task Zip_Extract_ZipSlip_PathTraversal_ThrowsSecurityException()
    {
        var maliciousZip = Path.Combine(_testDir, "slip.zip");
        TestArchiveFixtures.CreateZipSlipArchive(maliciousZip, "../../evil.txt", "evil payload");

        var extractDir = Path.Combine(_testDir, "slip_extracted");
        Directory.CreateDirectory(extractDir);

        var req = new ArchiveExtractionRequest(maliciousZip, extractDir);
        var ex = await Assert.ThrowsAsync<SecurityException>(() => _engine.ExtractAsync(req));
        Assert.Contains("Malicious entry detected", ex.Message);
    }

    [Fact]
    public async Task Zip_Extract_EncryptedZip_ThrowsNotSupportedException()
    {
        var encZip = Path.Combine(_testDir, "encrypted.zip");
        TestArchiveFixtures.CreateEncryptedZipArchive(encZip, "secret.txt", "classified data");

        var extractDir = Path.Combine(_testDir, "enc_extracted");

        var req = new ArchiveExtractionRequest(encZip, extractDir);
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => _engine.ExtractAsync(req));
        Assert.Contains("password-protected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Zip_ListEntries_EncryptedZip_FlagsEncryptedEntry()
    {
        var encZip = Path.Combine(_testDir, "encrypted_list.zip");
        TestArchiveFixtures.CreateEncryptedZipArchive(encZip, "secret.txt", "classified data");

        var entries = await _engine.ListEntriesAsync(encZip);
        Assert.Single(entries);
        Assert.True(entries[0].IsEncrypted);
    }

    [Fact]
    public async Task Zip_CompressAndExtract_UnicodeFilenames_Preserved()
    {
        var sourceDir = Path.Combine(_testDir, "unicode_src");
        Directory.CreateDirectory(sourceDir);
        var file = Path.Combine(sourceDir, "日本語_тест_🎉.txt");
        await File.WriteAllTextAsync(file, "Unicode zip content");

        var zipPath = Path.Combine(_testDir, "unicode.zip");
        var extractDir = Path.Combine(_testDir, "unicode_extracted");

        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        var extracted = Path.Combine(extractDir, "日本語_тест_🎉.txt");
        Assert.True(File.Exists(extracted));
        Assert.Equal("Unicode zip content", await File.ReadAllTextAsync(extracted));
    }

    [Fact]
    public async Task Zip_Compress_Cancellation_ThrowsOperationCanceledException()
    {
        var sourceDir = Path.Combine(_testDir, "cancel_zip_src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        var zipPath = Path.Combine(_testDir, "cancel.zip");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9), ct: cts.Token));
    }

    [Fact]
    public async Task Zip_Extract_Cancellation_ThrowsOperationCanceledException()
    {
        var sourceDir = Path.Combine(_testDir, "cancel_ext_src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        var zipPath = Path.Combine(_testDir, "cancel_ext.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, Path.Combine(_testDir, "out")), ct: cts.Token));
    }

    [Fact]
    public async Task Zip_ListEntries_Cancellation_ThrowsOperationCanceledException()
    {
        var sourceDir = Path.Combine(_testDir, "cancel_list_src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        var zipPath = Path.Combine(_testDir, "cancel_list.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _engine.ListEntriesAsync(zipPath, ct: cts.Token));
    }

    #endregion

    #region Gzip (.gz) Tests

    [Fact]
    public async Task Gz_Extract_DecompressesGzFileCorrectly()
    {
        var gzPath = Path.Combine(_testDir, "document.txt.gz");
        var content = "Decompressed GZ stream payload.";
        await TestArchiveFixtures.CreateGzArchiveAsync(gzPath, content);

        var extractDir = Path.Combine(_testDir, "gz_extracted");
        var progressReports = new List<ProgressReport>();

        await _engine.ExtractAsync(
            new ArchiveExtractionRequest(gzPath, extractDir),
            new SyncProgress<ProgressReport>(progressReports.Add));

        var extracted = Path.Combine(extractDir, "document.txt");
        Assert.True(File.Exists(extracted));
        Assert.Equal(content, await File.ReadAllTextAsync(extracted));
        Assert.NotEmpty(progressReports);
    }

    [Fact]
    public async Task Gz_ListEntries_ReturnsCorrectMetadata()
    {
        var gzPath = Path.Combine(_testDir, "payload.data.gz");
        var content = "Some gzip test content.";
        await TestArchiveFixtures.CreateGzArchiveAsync(gzPath, content);

        var entries = await _engine.ListEntriesAsync(gzPath);
        Assert.Single(entries);
        Assert.Equal("payload.data", entries[0].RelativePath);
        Assert.False(entries[0].IsDirectory);
        Assert.Equal(Encoding.UTF8.GetByteCount(content), entries[0].UncompressedSize);
    }

    [Fact]
    public async Task Gz_Extract_OverwriteFalse_WhenFileExists_ThrowsIOException()
    {
        var gzPath = Path.Combine(_testDir, "data.txt.gz");
        await TestArchiveFixtures.CreateGzArchiveAsync(gzPath, "new gz content");

        var extractDir = Path.Combine(_testDir, "gz_overwrite_dst");
        Directory.CreateDirectory(extractDir);
        await File.WriteAllTextAsync(Path.Combine(extractDir, "data.txt"), "pre-existing content");

        var req = new ArchiveExtractionRequest(gzPath, extractDir, Overwrite: false);
        await Assert.ThrowsAsync<IOException>(() => _engine.ExtractAsync(req));
    }

    [Fact]
    public async Task Gz_Extract_Cancellation_ThrowsOperationCanceledException()
    {
        var gzPath = Path.Combine(_testDir, "cancel.txt.gz");
        await TestArchiveFixtures.CreateGzArchiveAsync(gzPath, "test content");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(gzPath, Path.Combine(_testDir, "out")), ct: cts.Token));
    }

    #endregion

    #region Tar.Gz & .Tgz Tests

    [Fact]
    public async Task TarGz_Extract_DecompressesMultiFilesAndDirectories()
    {
        var tarGzPath = Path.Combine(_testDir, "archive.tar.gz");
        var files = new Dictionary<string, string>
        {
            ["file1.txt"] = "File 1 contents",
            ["nested/file2.txt"] = "Nested file 2 contents",
            ["nested/deep/file3.txt"] = "Deep file 3 contents"
        };
        await TestArchiveFixtures.CreateTarGzArchiveAsync(tarGzPath, files);

        var extractDir = Path.Combine(_testDir, "targz_extracted");
        var progressReports = new List<ProgressReport>();

        await _engine.ExtractAsync(
            new ArchiveExtractionRequest(tarGzPath, extractDir),
            new Progress<ProgressReport>(progressReports.Add));

        Assert.Equal("File 1 contents", await File.ReadAllTextAsync(Path.Combine(extractDir, "file1.txt")));
        Assert.Equal("Nested file 2 contents", await File.ReadAllTextAsync(Path.Combine(extractDir, "nested", "file2.txt")));
        Assert.Equal("Deep file 3 contents", await File.ReadAllTextAsync(Path.Combine(extractDir, "nested", "deep", "file3.txt")));
        Assert.NotEmpty(progressReports);
    }

    [Fact]
    public async Task Tgz_Extract_DecompressesCorrectly()
    {
        var tgzPath = Path.Combine(_testDir, "archive.tgz");
        var files = new Dictionary<string, string>
        {
            ["tgz_file.txt"] = "TGZ file content"
        };
        await TestArchiveFixtures.CreateTarGzArchiveAsync(tgzPath, files);

        var extractDir = Path.Combine(_testDir, "tgz_extracted");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(tgzPath, extractDir));

        var extracted = Path.Combine(extractDir, "tgz_file.txt");
        Assert.True(File.Exists(extracted));
        Assert.Equal("TGZ file content", await File.ReadAllTextAsync(extracted));
    }

    [Fact]
    public async Task TarGz_ListEntries_ReturnsAllEntries()
    {
        var tarGzPath = Path.Combine(_testDir, "list.tar.gz");
        var files = new Dictionary<string, string>
        {
            ["alpha.txt"] = "Alpha",
            ["beta/bravo.txt"] = "Bravo"
        };
        await TestArchiveFixtures.CreateTarGzArchiveAsync(tarGzPath, files);

        var entries = await _engine.ListEntriesAsync(tarGzPath);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.RelativePath == "alpha.txt" && !e.IsDirectory);
        Assert.Contains(entries, e => e.RelativePath == "beta/bravo.txt" && !e.IsDirectory);
    }

    [Fact]
    public async Task TarGz_Extract_TarSlip_PathTraversal_ThrowsSecurityException()
    {
        var maliciousTarGz = Path.Combine(_testDir, "malicious.tar.gz");
        await TestArchiveFixtures.CreateTarSlipArchiveAsync(maliciousTarGz, "../../../malicious_file.txt");

        var extractDir = Path.Combine(_testDir, "targz_slip_dst");
        Directory.CreateDirectory(extractDir);

        var req = new ArchiveExtractionRequest(maliciousTarGz, extractDir);
        var ex = await Assert.ThrowsAsync<SecurityException>(() => _engine.ExtractAsync(req));
        Assert.Contains("Malicious entry detected", ex.Message);
    }

    [Fact]
    public async Task TarGz_Extract_OverwriteFalse_WhenFileExists_ThrowsIOException()
    {
        var tarGzPath = Path.Combine(_testDir, "overwrite.tar.gz");
        var files = new Dictionary<string, string> { ["item.txt"] = "new item" };
        await TestArchiveFixtures.CreateTarGzArchiveAsync(tarGzPath, files);

        var extractDir = Path.Combine(_testDir, "targz_overwrite_dst");
        Directory.CreateDirectory(extractDir);
        await File.WriteAllTextAsync(Path.Combine(extractDir, "item.txt"), "old item");

        var req = new ArchiveExtractionRequest(tarGzPath, extractDir, Overwrite: false);
        await Assert.ThrowsAsync<IOException>(() => _engine.ExtractAsync(req));
    }

    [Fact]
    public async Task TarGz_Extract_Cancellation_ThrowsOperationCanceledException()
    {
        var tarGzPath = Path.Combine(_testDir, "cancel.tar.gz");
        await TestArchiveFixtures.CreateTarGzArchiveAsync(tarGzPath, new Dictionary<string, string> { ["f.txt"] = "c" });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(tarGzPath, Path.Combine(_testDir, "out")), ct: cts.Token));
    }

    #endregion

    #region 7-Zip (.7z) Tests

    [Fact]
    public async Task SevenZip_ListAndExtract_PreservesContent()
    {
        var sevenZipPath = Path.Combine(_testDir, "sample.7z");
        TestArchiveFixtures.CreateSevenZipArchive(sevenZipPath, "sample.txt", "Hello 7-Zip decompressor!");

        var entries = await _engine.ListEntriesAsync(sevenZipPath);
        Assert.Single(entries);
        Assert.Equal("sample.txt", entries[0].RelativePath);

        var extractDir = Path.Combine(_testDir, "7z_extracted");
        var progressReports = new List<ProgressReport>();
        await _engine.ExtractAsync(
            new ArchiveExtractionRequest(sevenZipPath, extractDir),
            new Progress<ProgressReport>(progressReports.Add));

        var extracted = Path.Combine(extractDir, "sample.txt");
        Assert.True(File.Exists(extracted));
        Assert.Equal("Hello 7-Zip decompressor!", await File.ReadAllTextAsync(extracted));
        Assert.NotEmpty(progressReports);
    }

    [Fact]
    public async Task SevenZip_Extract_EncryptedArchive_ThrowsNotSupportedException()
    {
        var enc7z = Path.Combine(_testDir, "encrypted.7z");
        TestArchiveFixtures.CreateEncryptedSevenZipArchive(enc7z);

        var extractDir = Path.Combine(_testDir, "7z_enc_dst");
        var req = new ArchiveExtractionRequest(enc7z, extractDir);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => _engine.ExtractAsync(req));
        Assert.Contains("password-protected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SevenZip_ListEntries_EncryptedArchive_ThrowsNotSupportedException()
    {
        var enc7z = Path.Combine(_testDir, "encrypted_list.7z");
        TestArchiveFixtures.CreateEncryptedSevenZipArchive(enc7z);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => _engine.ListEntriesAsync(enc7z));
        Assert.Contains("password-protected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SevenZip_Extract_PathTraversal_ThrowsSecurityException()
    {
        var slip7z = Path.Combine(_testDir, "slip.7z");
        TestArchiveFixtures.CreateSevenZipSlipArchive(slip7z, "../../malicious_7z.txt");

        var extractDir = Path.Combine(_testDir, "7z_slip_dst");
        Directory.CreateDirectory(extractDir);

        var req = new ArchiveExtractionRequest(slip7z, extractDir);
        var ex = await Assert.ThrowsAsync<SecurityException>(() => _engine.ExtractAsync(req));
        Assert.Contains("Malicious entry detected", ex.Message);
    }

    #endregion

    #region RAR Tests (RAR4 & RAR5)

    [Fact]
    public async Task Rar4_ListAndExtract_PreservesContent()
    {
        var rarPath = Path.Combine(_testDir, "legacy.rar");
        TestArchiveFixtures.CreateRar4Archive(rarPath, "legacy.txt", "Hello RAR4 Archive!");

        var entries = await _engine.ListEntriesAsync(rarPath);
        Assert.Single(entries);
        Assert.Equal("legacy.txt", entries[0].RelativePath);

        var extractDir = Path.Combine(_testDir, "rar4_extracted");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(rarPath, extractDir));

        var extracted = Path.Combine(extractDir, "legacy.txt");
        Assert.True(File.Exists(extracted));
        Assert.Equal("Hello RAR4 Archive!", await File.ReadAllTextAsync(extracted));
    }

    [Fact]
    public async Task Rar4_Extract_EncryptedArchive_ThrowsNotSupportedException()
    {
        var encRar = Path.Combine(_testDir, "encrypted_rar4.rar");
        TestArchiveFixtures.CreateRar4Archive(encRar, "locked.txt", "secret", encrypted: true);

        var extractDir = Path.Combine(_testDir, "rar4_enc_dst");
        var req = new ArchiveExtractionRequest(encRar, extractDir);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => _engine.ExtractAsync(req));
        Assert.Contains("password-protected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rar4_Extract_MultiVolumeIncomplete_ThrowsInvalidOperationException()
    {
        var mvRar = Path.Combine(_testDir, "part1.rar");
        TestArchiveFixtures.CreateRar4Archive(mvRar, "part.txt", "part data", multiVolume: true);

        var extractDir = Path.Combine(_testDir, "rar4_mv_dst");
        var req = new ArchiveExtractionRequest(mvRar, extractDir);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _engine.ExtractAsync(req));
        Assert.Contains("Multi-volume RAR archive is missing subsequent volume parts", ex.Message);
    }

    [Fact]
    public async Task Rar5_ListAndExtract_PreservesContent()
    {
        var rar5Path = Path.Combine(_testDir, "modern.rar");
        TestArchiveFixtures.CreateRar5Archive(rar5Path, "modern.txt", "Hello RAR5 Archive!");

        var entries = await _engine.ListEntriesAsync(rar5Path);
        Assert.Single(entries);
        Assert.Equal("modern.txt", entries[0].RelativePath);

        var extractDir = Path.Combine(_testDir, "rar5_extracted");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(rar5Path, extractDir));

        var extracted = Path.Combine(extractDir, "modern.txt");
        Assert.True(File.Exists(extracted));
        Assert.Equal("Hello RAR5 Archive!", await File.ReadAllTextAsync(extracted));
    }

    [Fact]
    public async Task Rar5_Extract_EncryptedArchive_ThrowsNotSupportedException()
    {
        var encRar5 = Path.Combine(_testDir, "encrypted_rar5.rar");
        TestArchiveFixtures.CreateRar5Archive(encRar5, "locked5.txt", "secret5", encrypted: true);

        var extractDir = Path.Combine(_testDir, "rar5_enc_dst");
        var req = new ArchiveExtractionRequest(encRar5, extractDir);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => _engine.ExtractAsync(req));
        Assert.Contains("password-protected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}

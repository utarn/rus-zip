using System.Diagnostics;
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

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
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
        var progress = new SynchronousProgress<ProgressReport>(progressReports.Add);

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
    public async Task Zip_CompressAndExtract_LargeFile_ChunkedProgress()
    {
        // NOTE: this test covers chunked reading and progress reporting for a large single entry.
        // It deliberately does NOT cross a Zip64 boundary — Zip64 engages only >4 GB per entry or
        // >65535 entries per archive. The entry-count boundary is covered by
        // Zip_CompressAndExtract_ManyTinyEntries_CrossesZip64Boundary below; the per-entry >4 GB
        // case is out of scope for the test suite entirely (it would need ~4 GB of disk + RAM and
        // minutes of compression for a single entry — see the DoD note for issue #55).
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
            new SynchronousProgress<ProgressReport>(compressProgress.Add));

        // Act - Extract
        await _engine.ExtractAsync(
            new ArchiveExtractionRequest(zipPath, extractDir),
            new SynchronousProgress<ProgressReport>(extractProgress.Add));

        // Assert
        var extractedFile = Path.Combine(extractDir, "large.bin");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal(payload, await File.ReadAllBytesAsync(extractedFile));

        // Chunked 80KB stream reading yields multiple progress reports
        Assert.True(compressProgress.Count > 1, $"Expected multiple progress reports during compression, got {compressProgress.Count}");
        Assert.True(extractProgress.Count > 1, $"Expected multiple progress reports during extraction, got {extractProgress.Count}");
    }

    [Fact]
    public async Task Zip_CompressAndExtract_ManyTinyEntries_CrossesZip64Boundary()
    {
        // F-24 regression: ZipWriter sets UseZip64 = true (SharpCompressArchiveEngine) but no test
        // ever crossed a Zip64 boundary — Zip64 engages only when a single entry exceeds 4 GB or
        // when the archive holds more than 65535 entries. The per-entry >4 GB case is out of scope
        // (disk/time, see the comment on Zip_CompressAndExtract_LargeFile_ChunkedProgress), so this
        // test crosses the entry-count boundary: 65,536 + 32 one-byte files.
        var sw = Stopwatch.StartNew();
        const int entryCount = 65_536 + 32; // classic EOCD total-entry count is a ushort (max 65535)

        var sourceDir = Path.Combine(_testDir, "many_entries_src");
        Directory.CreateDirectory(sourceDir);

        var payload = new byte[1] { 0x5A };
        for (int i = 0; i < entryCount; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(sourceDir, $"f{i:D5}.dat"), payload);
        }

        var zipPath = Path.Combine(_testDir, "many_entries.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 1));

        // >65535 entries cannot be represented in the classic EOCD total-entry field, so the writer
        // must emit Zip64 structures (a Zip64 EOCD locator and/or the 0xFFFF sentinel).
        var entries = await _engine.ListEntriesAsync(zipPath);
        Assert.True(entries.Count > 65_535, $"Expected >65535 entries, got {entries.Count}");
        AssertZipUsesZip64(zipPath);

        var extractDir = Path.Combine(_testDir, "many_entries_extracted");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));
        Assert.Equal(entryCount, result.FilesExtracted);

        // Sampled contents round-trip intact, including the boundary indices around 65535.
        foreach (int i in new[] { 0, 1, 65_534, 65_535, entryCount - 1 })
        {
            Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(extractDir, $"f{i:D5}.dat")));
        }

        sw.Stop();
        // Runtime budget for this test (DoD for issue #55): a 65k-entry corpus must stay under 60 s.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), $"Zip64 entry-count test exceeded 60s budget: {sw.Elapsed.TotalSeconds:F1}s");
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
    public async Task Zip_Compress_NonAsciiNames_SetEfsFlag_ThirdPartyReadable()
    {
        // F-12: CLI-written zips must set the language-encoding flag (bit 11 / 0x800) so third-party
        // readers (python zipfile, unzip) decode non-ASCII names as UTF-8 instead of mojibake.
        var sourceDir = Path.Combine(_testDir, "efs_src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "файл_🎉.txt"), "content");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "ascii.txt"), "content");

        var zipPath = Path.Combine(_testDir, "efs.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));

        var flags = ReadZipCentralDirectoryFlags(zipPath);
        Assert.True(flags.ContainsKey("файл_🎉.txt"), "Archive must contain the non-ASCII entry name");
        Assert.True((flags["файл_🎉.txt"] & 0x800) != 0, "EFS flag (bit 11) must be set for non-ASCII names");
        Assert.True((flags["ascii.txt"] & 0x800) != 0, "EFS flag (bit 11) must be set for all names");
    }

    [Fact]
    public async Task Zip_RoundTrip_ExecutableBit_Preserved()
    {
        if (OperatingSystem.IsWindows())
            return;

        var sourceDir = Path.Combine(_testDir, "exec_src");
        Directory.CreateDirectory(sourceDir);
        var execFile = Path.Combine(sourceDir, "run.sh");
        await File.WriteAllTextAsync(execFile, "#!/bin/sh\necho hi\n");

        var execMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                       UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                       UnixFileMode.OtherRead | UnixFileMode.OtherExecute; // 0755
        File.SetUnixFileMode(execFile, execMode);

        var zipPath = Path.Combine(_testDir, "exec.zip");
        var extractDir = Path.Combine(_testDir, "exec_extracted");

        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        var extracted = Path.Combine(extractDir, "run.sh");
        Assert.True(File.Exists(extracted));
        Assert.Equal(execMode, File.GetUnixFileMode(extracted));
    }

    [Fact]
    public async Task Zip_RoundTrip_RegularFileMode644_Preserved()
    {
        if (OperatingSystem.IsWindows())
            return;

        var sourceDir = Path.Combine(_testDir, "regular_src");
        Directory.CreateDirectory(sourceDir);
        var regularFile = Path.Combine(sourceDir, "data.txt");
        await File.WriteAllTextAsync(regularFile, "plain data");

        var regularMode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                          UnixFileMode.GroupRead | UnixFileMode.OtherRead; // 0644
        File.SetUnixFileMode(regularFile, regularMode);

        var zipPath = Path.Combine(_testDir, "regular.zip");
        var extractDir = Path.Combine(_testDir, "regular_extracted");

        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        var extracted = Path.Combine(extractDir, "data.txt");
        Assert.True(File.Exists(extracted));
        Assert.Equal(regularMode, File.GetUnixFileMode(extracted));
    }

    [Fact]
    public async Task Zip_Extract_PythonCraftedExecutable_RestoresMode()
    {
        if (OperatingSystem.IsWindows())
            return;

        // A python zip created on Unix carries external_attr = (S_IFREG|0o755)<<16 and a Unix
        // "version made by" byte. The reader must translate that into UnixMode for restoration (F-13).
        var zipBytes = TestArchiveFixtures.BuildZipWithUnixMode("run.sh", "#!/bin/sh\n", 0x1ED); // 0o755
        var zipPath = Path.Combine(_testDir, "python_exec.zip");
        await File.WriteAllBytesAsync(zipPath, zipBytes);
        var extractDir = Path.Combine(_testDir, "python_exec_extracted");

        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        var extracted = Path.Combine(extractDir, "run.sh");
        Assert.True(File.Exists(extracted));
        Assert.Equal("#!/bin/sh\n", await File.ReadAllTextAsync(extracted));

        var execMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                       UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                       UnixFileMode.OtherRead | UnixFileMode.OtherExecute; // 0755
        Assert.Equal(execMode, File.GetUnixFileMode(extracted));
    }

    [Fact]
    public async Task Zip_Extract_ModeLessZip_UsesDefaultPermissions()
    {
        if (OperatingSystem.IsWindows())
            return;

        // A Windows-created zip has DOS external attributes and a DOS "version made by" byte, so no
        // POSIX mode must be applied — extraction succeeds with the process's default permissions.
        var zipBytes = TestArchiveFixtures.BuildStoreZip(("win.txt", "win content"));
        var zipPath = Path.Combine(_testDir, "modeless.zip");
        await File.WriteAllBytesAsync(zipPath, zipBytes);
        var extractDir = Path.Combine(_testDir, "modeless_extracted");

        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        var extracted = Path.Combine(extractDir, "win.txt");
        Assert.True(File.Exists(extracted));
        Assert.Equal("win content", await File.ReadAllTextAsync(extracted));

        // No POSIX mode is stored, so extraction must leave the process's default permissions
        // (umask-derived) intact — not a bogus value decoded from DOS attribute bits.
        var mode = File.GetUnixFileMode(extracted);
        Assert.True(mode.HasFlag(UnixFileMode.UserRead));
        Assert.True(mode.HasFlag(UnixFileMode.UserWrite), "A Windows-created zip must extract writable by default");

        var reference = Path.Combine(_testDir, "reference.txt");
        await File.WriteAllTextAsync(reference, "ref");
        Assert.Equal(File.GetUnixFileMode(reference), mode);
    }

    [Fact]
    public async Task Zip_Extract_SharpCompressDosZip_DoesNotApplyBogusMode()
    {
        if (OperatingSystem.IsWindows())
            return;

        // Pre-fix CLI zips (and other DOS-created zips) carry external_attr = 0x81000000 with a DOS
        // "version made by" byte. Reading external_attr >> 16 without a create-system check would yield
        // 0o400; the reader must reject it and fall back to default permissions.
        var zipBytes = TestArchiveFixtures.BuildZipWithCentralDirectoryMetadata("sc.txt", "sc content", 0x002d, 0x81000000);
        var zipPath = Path.Combine(_testDir, "sc_dos.zip");
        await File.WriteAllBytesAsync(zipPath, zipBytes);
        var extractDir = Path.Combine(_testDir, "sc_dos_extracted");

        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        var extracted = Path.Combine(extractDir, "sc.txt");
        Assert.True(File.Exists(extracted));
        var mode = File.GetUnixFileMode(extracted);
        Assert.NotEqual((UnixFileMode)0x100, mode); // not 0o400
        Assert.True(mode.HasFlag(UnixFileMode.UserWrite), "A Windows-created zip must extract writable by default");
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

    [Fact]
    public async Task Zip_Extract_LocalDataPatched_ThrowsArchiveIntegrityException_AndCleansUpPartialFile()
    {
        // F-09: a byte-patched local-data region previously extracted silently with exit 0.
        var validZip = TestArchiveFixtures.BuildStoreZip(("a.txt", "AAAAA"), ("b.txt", "BBBBBBBBBB"));
        var patchedZip = TestArchiveFixtures.FlipByteInFirstLocalData(validZip);

        var zipPath = Path.Combine(_testDir, "patched.zip");
        await File.WriteAllBytesAsync(zipPath, patchedZip);
        var extractDir = Path.Combine(_testDir, "patched_out");

        var ex = await Assert.ThrowsAsync<ArchiveIntegrityException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir)));

        Assert.Equal("a.txt", ex.EntryName);
        Assert.Contains("CRC-32 mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
        // The corrupt partial output must be deleted.
        Assert.False(File.Exists(Path.Combine(extractDir, "a.txt")));
        Assert.Empty(Directory.GetFiles(extractDir));
    }

    [Fact]
    public async Task Zip_List_UnparseableCentralDirectory_ThrowsArchiveIntegrityException()
    {
        // F-10: EOCD declares entries but the central directory is zeroed — must be an error,
        // not a silent empty-success list.
        var validZip = TestArchiveFixtures.BuildStoreZip(("a.txt", "AAAAA"), ("b.txt", "BBBBBBBBBB"));
        var brokenZip = TestArchiveFixtures.ZeroCentralDirectoryRegion(validZip);

        var zipPath = Path.Combine(_testDir, "broken_cd.zip");
        await File.WriteAllBytesAsync(zipPath, brokenZip);

        var ex = await Assert.ThrowsAsync<ArchiveIntegrityException>(() =>
            _engine.ListEntriesAsync(zipPath));

        Assert.Contains("central directory", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Zip_Extract_UnparseableCentralDirectory_ThrowsArchiveIntegrityException()
    {
        var validZip = TestArchiveFixtures.BuildStoreZip(("a.txt", "AAAAA"), ("b.txt", "BBBBBBBBBB"));
        var brokenZip = TestArchiveFixtures.ZeroCentralDirectoryRegion(validZip);

        var zipPath = Path.Combine(_testDir, "broken_cd_extract.zip");
        await File.WriteAllBytesAsync(zipPath, brokenZip);
        var extractDir = Path.Combine(_testDir, "broken_cd_out");

        var ex = await Assert.ThrowsAsync<ArchiveIntegrityException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir)));

        Assert.Contains("central directory", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(extractDir));
    }

    [Fact]
    public async Task Zip_List_BrokenEocd_ThrowsArchiveIntegrityException()
    {
        // A corrupted EOCD signature leaves no locatable end-of-central-directory record.
        var validZip = TestArchiveFixtures.BuildStoreZip(("a.txt", "AAAAA"));
        var brokenZip = TestArchiveFixtures.CorruptEocdSignature(validZip);

        var zipPath = Path.Combine(_testDir, "broken_eocd.zip");
        await File.WriteAllBytesAsync(zipPath, brokenZip);

        var ex = await Assert.ThrowsAsync<ArchiveIntegrityException>(() =>
            _engine.ListEntriesAsync(zipPath));

        Assert.Contains("corrupted or unparseable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Zip_Extract_BrokenEocd_ThrowsArchiveIntegrityException()
    {
        var validZip = TestArchiveFixtures.BuildStoreZip(("a.txt", "AAAAA"));
        var brokenZip = TestArchiveFixtures.CorruptEocdSignature(validZip);

        var zipPath = Path.Combine(_testDir, "broken_eocd_extract.zip");
        await File.WriteAllBytesAsync(zipPath, brokenZip);
        var extractDir = Path.Combine(_testDir, "broken_eocd_out");

        var ex = await Assert.ThrowsAsync<ArchiveIntegrityException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir)));

        Assert.Contains("corrupted or unparseable", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(extractDir));
    }

    [Fact]
    public async Task Zip_Extract_NameLengthCorruption_ThrowsArchiveIntegrityException_AndCleansUp()
    {
        // A local header name length pointing past EOF makes the entry stream unresolvable.
        var validZip = TestArchiveFixtures.BuildStoreZip(("a.txt", "AAAAA"), ("b.txt", "BBBBBBBBBB"));
        var brokenZip = TestArchiveFixtures.CorruptFirstNameLength(validZip);

        var zipPath = Path.Combine(_testDir, "name_len.zip");
        await File.WriteAllBytesAsync(zipPath, brokenZip);
        var extractDir = Path.Combine(_testDir, "name_len_out");

        var ex = await Assert.ThrowsAsync<ArchiveIntegrityException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir)));

        Assert.Equal("a.txt", ex.EntryName);
        Assert.Contains("corrupted", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(extractDir, "a.txt")));
        Assert.Empty(Directory.GetFiles(extractDir));
    }

    [Fact]
    public async Task Zip_Extract_EntryNameWithNullCharacter_ThrowsArchiveIntegrityException_AndCleansUp()
    {
        // F-10 (translation half): a corrupt archive whose entry name contains a NUL byte surfaces as
        // ArgumentException ("Null character in path") during path resolution. Because the failure
        // originates from archive data (engine boundary) — not a user-supplied bad path — the engine
        // must translate it to ArchiveIntegrityException (→ EXECUTION_ERROR, exit 1), never leak the
        // raw ArgumentException (which the CLI would map to ARGUMENT_ERROR, exit 2).
        var zipPath = Path.Combine(_testDir, "null_char.zip");
        TestArchiveFixtures.CreateZipArchiveWithEntryName(zipPath, "bad\u0000name.txt", "payload");
        var extractDir = Path.Combine(_testDir, "null_char_out");

        var ex = await Assert.ThrowsAsync<ArchiveIntegrityException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir)));

        Assert.Contains("invalid path", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(extractDir));
    }

    [Fact]
    public async Task Zip_EmptyArchive_ListReturnsZeroEntries_AndExtractCompletes()
    {
        // A genuinely empty zip (EOCD with zero entries and zero CD size) must keep working.
        var emptyZip = TestArchiveFixtures.BuildStoreZip();

        var zipPath = Path.Combine(_testDir, "empty.zip");
        await File.WriteAllBytesAsync(zipPath, emptyZip);

        var entries = await _engine.ListEntriesAsync(zipPath);
        Assert.Empty(entries);

        var extractDir = Path.Combine(_testDir, "empty_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));
        Assert.Equal(0, result.FilesExtracted);
    }

    [Fact]
    public async Task Zip_Roundtrip_CrcVerifiedWhileStreaming_NoSecondPass_RemainsByteIdentical()
    {
        // Valid archives extract byte-identically with CRC verified during the single streaming pass.
        var sourceDir = Path.Combine(_testDir, "crc_rt_src");
        Directory.CreateDirectory(sourceDir);
        var payload = new byte[256 * 1024];
        new Random(999).NextBytes(payload);
        await File.WriteAllBytesAsync(Path.Combine(sourceDir, "data.bin"), payload);

        var zipPath = Path.Combine(_testDir, "crc_rt.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));

        var extractDir = Path.Combine(_testDir, "crc_rt_out");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(extractDir, "data.bin")));
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
            new SynchronousProgress<ProgressReport>(progressReports.Add));

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
            new SynchronousProgress<ProgressReport>(progressReports.Add));

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

    #region Selective extraction (entry filter)

    [Fact]
    public async Task Zip_Extract_WithSingleFileFilter_ExtractsOnlyThatFile()
    {
        var sourceDir = Path.Combine(_testDir, "zip_filter_single_src");
        Directory.CreateDirectory(Path.Combine(sourceDir, "folder"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "b.txt"), "beta");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "folder", "c.txt"), "gamma");

        var zipPath = Path.Combine(_testDir, "zip_filter_single.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));

        var extractDir = Path.Combine(_testDir, "zip_filter_single_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir, Entries: ["b.txt"]));

        Assert.Equal(1, result.FilesExtracted);
        Assert.Equal("beta", await File.ReadAllTextAsync(Path.Combine(extractDir, "b.txt")));
        Assert.False(File.Exists(Path.Combine(extractDir, "a.txt")));
        Assert.False(Directory.Exists(Path.Combine(extractDir, "folder")));
    }

    [Fact]
    public async Task Zip_Extract_WithFolderSubtreeFilter_ExtractsSubtreeOnly()
    {
        var sourceDir = Path.Combine(_testDir, "zip_filter_folder_src");
        Directory.CreateDirectory(Path.Combine(sourceDir, "sub", "deep"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "root.txt"), "root");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "sub", "one.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "sub", "deep", "two.txt"), "two");

        var zipPath = Path.Combine(_testDir, "zip_filter_folder.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));

        var extractDir = Path.Combine(_testDir, "zip_filter_folder_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir, Entries: ["sub"]));

        Assert.Equal(2, result.FilesExtracted);
        Assert.True(File.Exists(Path.Combine(extractDir, "sub", "one.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "sub", "deep", "two.txt")));
        Assert.False(File.Exists(Path.Combine(extractDir, "root.txt")));
    }

    [Fact]
    public async Task Zip_Extract_WithNoMatchFilter_ThrowsInvalidOperationException()
    {
        var sourceDir = Path.Combine(_testDir, "zip_filter_nomatch_src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "alpha");

        var zipPath = Path.Combine(_testDir, "zip_filter_nomatch.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));

        var extractDir = Path.Combine(_testDir, "zip_filter_nomatch_out");
        var req = new ArchiveExtractionRequest(zipPath, extractDir, Entries: ["nonexistent.txt"]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _engine.ExtractAsync(req));
        Assert.Contains("No archive entries matched", ex.Message);
        Assert.Empty(Directory.GetFileSystemEntries(extractDir));
    }

    [Fact]
    public async Task Zip_Extract_FilterWithTraversalName_StillRefused()
    {
        var zipPath = Path.Combine(_testDir, "zip_filter_slip.zip");
        TestArchiveFixtures.CreateZipSlipArchive(zipPath, "../../evil.txt");

        var extractDir = Path.Combine(_testDir, "zip_filter_slip_out");
        Directory.CreateDirectory(extractDir);

        // The filter matches the malicious entry, but SafeArchiveExtractor must still refuse it.
        var req = new ArchiveExtractionRequest(zipPath, extractDir, Entries: ["../../evil.txt"]);
        var ex = await Assert.ThrowsAsync<SecurityException>(() => _engine.ExtractAsync(req));
        Assert.Contains("Malicious entry detected", ex.Message);
    }

    [Fact]
    public async Task Zip_Extract_NullFilter_ExtractsAllEntries()
    {
        var sourceDir = Path.Combine(_testDir, "zip_filter_null_src");
        Directory.CreateDirectory(Path.Combine(sourceDir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "sub", "b.txt"), "beta");

        var zipPath = Path.Combine(_testDir, "zip_filter_null.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 9));

        var extractDir = Path.Combine(_testDir, "zip_filter_null_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir, Entries: null));

        Assert.Equal(2, result.FilesExtracted);
        Assert.True(File.Exists(Path.Combine(extractDir, "a.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "sub", "b.txt")));
    }

    [Fact]
    public async Task TarGz_Extract_WithSingleFileFilter_ExtractsOnlyThatFile()
    {
        var tarGzPath = Path.Combine(_testDir, "targz_filter_single.tar.gz");
        var files = new Dictionary<string, string>
        {
            ["file1.txt"] = "one",
            ["nested/file2.txt"] = "two"
        };
        await TestArchiveFixtures.CreateTarGzArchiveAsync(tarGzPath, files);

        var extractDir = Path.Combine(_testDir, "targz_filter_single_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(tarGzPath, extractDir, Entries: ["nested/file2.txt"]));

        Assert.Equal(1, result.FilesExtracted);
        Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(extractDir, "nested", "file2.txt")));
        Assert.False(File.Exists(Path.Combine(extractDir, "file1.txt")));
    }

    [Fact]
    public async Task TarGz_Extract_WithFolderSubtreeFilter_ExtractsSubtreeOnly()
    {
        var tarGzPath = Path.Combine(_testDir, "targz_filter_folder.tar.gz");
        var files = new Dictionary<string, string>
        {
            ["root.txt"] = "root",
            ["sub/one.txt"] = "one",
            ["sub/deep/two.txt"] = "two"
        };
        await TestArchiveFixtures.CreateTarGzArchiveAsync(tarGzPath, files);

        var extractDir = Path.Combine(_testDir, "targz_filter_folder_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(tarGzPath, extractDir, Entries: ["sub"]));

        Assert.Equal(2, result.FilesExtracted);
        Assert.True(File.Exists(Path.Combine(extractDir, "sub", "one.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "sub", "deep", "two.txt")));
        Assert.False(File.Exists(Path.Combine(extractDir, "root.txt")));
    }

    [Fact]
    public async Task TarGz_Extract_WithNoMatchFilter_ThrowsInvalidOperationException()
    {
        var tarGzPath = Path.Combine(_testDir, "targz_filter_nomatch.tar.gz");
        var files = new Dictionary<string, string> { ["file1.txt"] = "one" };
        await TestArchiveFixtures.CreateTarGzArchiveAsync(tarGzPath, files);

        var extractDir = Path.Combine(_testDir, "targz_filter_nomatch_out");
        var req = new ArchiveExtractionRequest(tarGzPath, extractDir, Entries: ["nonexistent.txt"]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _engine.ExtractAsync(req));
        Assert.Contains("No archive entries matched", ex.Message);
    }

    [Fact]
    public async Task Gz_Extract_WithMatchingFilter_ExtractsFile()
    {
        var gzPath = Path.Combine(_testDir, "doc.txt.gz");
        await TestArchiveFixtures.CreateGzArchiveAsync(gzPath, "gz filtered content");

        var extractDir = Path.Combine(_testDir, "gz_filter_match_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(gzPath, extractDir, Entries: ["doc.txt"]));

        Assert.Equal(1, result.FilesExtracted);
        Assert.Equal("gz filtered content", await File.ReadAllTextAsync(Path.Combine(extractDir, "doc.txt")));
    }

    [Fact]
    public async Task Gz_Extract_WithNonMatchingFilter_ThrowsInvalidOperationException()
    {
        var gzPath = Path.Combine(_testDir, "doc.txt.gz");
        await TestArchiveFixtures.CreateGzArchiveAsync(gzPath, "gz content");

        var extractDir = Path.Combine(_testDir, "gz_filter_nomatch_out");
        var req = new ArchiveExtractionRequest(gzPath, extractDir, Entries: ["other.txt"]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _engine.ExtractAsync(req));
        Assert.Contains("No archive entries matched", ex.Message);
    }

    #endregion

    #region Encryption classifier (IsPasswordOrEncryptedException)

    [Fact]
    public void IsPasswordOrEncryptedException_TypedSharpCompressCryptoException_ReturnsTrue()
    {
        // F-18: typed signals are classified first (SharpCompress AES-encrypted 7z/RAR5).
        var ex = new SharpCompress.Common.CryptographicException("Encrypted Rar archive has no password specified.");

        Assert.True(SharpCompressArchiveEngine.IsPasswordOrEncryptedException(ex));
    }

    [Fact]
    public void IsPasswordOrEncryptedException_TypedBclCryptoException_ReturnsTrue()
    {
        // F-18: BCL crypto primitive failures (wrong password → AES padding/decryption error) are typed too.
        var ex = new System.Security.Cryptography.CryptographicException("Padding is invalid and cannot be removed.");

        Assert.True(SharpCompressArchiveEngine.IsPasswordOrEncryptedException(ex));
    }

    [Fact]
    public void IsPasswordOrEncryptedException_UnrelatedTypeWithPasswordInMessage_ReturnsFalse()
    {
        // F-18: an unrelated exception whose message merely contains "password" must NOT be
        // misclassified as an encrypted archive (that would wrongly map it to UNSUPPORTED_FORMAT).
        var ex = new InvalidOperationException("The password provided is incorrect.");

        Assert.False(SharpCompressArchiveEngine.IsPasswordOrEncryptedException(ex));
    }

    [Fact]
    public void IsPasswordOrEncryptedException_UnrelatedTypeWithEncryptedInMessage_ReturnsFalse()
    {
        // F-18: same guard for "encrypted" — the message heuristic must stay narrow.
        var ex = new InvalidDataException("This archive looks encrypted but is actually malformed.");

        Assert.False(SharpCompressArchiveEngine.IsPasswordOrEncryptedException(ex));
    }

    [Fact]
    public void IsPasswordOrEncryptedException_SharpCompressNoPasswordSpecifiedMessage_ReturnsTrue()
    {
        // F-18: the narrowed fallback still recognizes SharpCompress's actual "no password specified"
        // wording even when the typed signal has been lost (e.g. rethrown as a generic wrapper).
        var ex = new InvalidOperationException("Encrypted 7Zip archive has no password specified.");

        Assert.True(SharpCompressArchiveEngine.IsPasswordOrEncryptedException(ex));
    }

    [Fact]
    public void IsPasswordOrEncryptedException_ArgumentNullExceptionWithoutDecoderRegistryStack_ReturnsFalse()
    {
        // F-18: a bare ArgumentNullException (no DecoderRegistry in the stack) is not an encryption signal.
        var ex = new ArgumentNullException("info");

        Assert.False(SharpCompressArchiveEngine.IsPasswordOrEncryptedException(ex));
    }

    [Fact]
    public void IsPasswordOrEncryptedException_ArgumentNullExceptionFromDecoderRegistry_ReturnsTrue()
    {
        // F-18: SharpCompress surfaces a missing crypto-info block as ArgumentNullException from its
        // DecoderRegistry (no typed signal) — the stack-based fallback recognizes it.
        var ex = DecoderRegistry.CreateMissingInfoException();

        Assert.True(SharpCompressArchiveEngine.IsPasswordOrEncryptedException(ex));
    }

    [Fact]
    public void IsPasswordOrEncryptedException_NestedInnerTypedException_ReturnsTrue()
    {
        // F-18: the classifier recurses into the inner chain, where SharpCompress usually wraps the root cause.
        var ex = new InvalidOperationException("Outer wrapper", new SharpCompress.Common.CryptographicException("Encrypted Rar archive has no password specified."));

        Assert.True(SharpCompressArchiveEngine.IsPasswordOrEncryptedException(ex));
    }

    /// <summary>
    /// Produces an <see cref="ArgumentNullException"/> whose stack trace passes through a type named
    /// <c>DecoderRegistry</c>, mirroring SharpCompress's missing-crypto-info signal (F-18).
    /// </summary>
    private static class DecoderRegistry
    {
        public static ArgumentNullException CreateMissingInfoException()
        {
            try
            {
                throw new ArgumentNullException("info");
            }
            catch (ArgumentNullException ex)
            {
                return ex;
            }
        }
    }

    #endregion

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    /// <summary>
    /// Parses the central directory of a zip and returns each entry name → its general-purpose flag
    /// bits. Used to assert the UTF-8/Efs flag (bit 11 / 0x800) that third-party readers rely on.
    /// </summary>
    private static Dictionary<string, ushort> ReadZipCentralDirectoryFlags(string zipPath)
    {
        var bytes = File.ReadAllBytes(zipPath);
        var result = new Dictionary<string, ushort>(StringComparer.Ordinal);

        int eocd = -1;
        for (int i = bytes.Length - 22; i >= 0; i--)
        {
            if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x05 && bytes[i + 3] == 0x06)
            {
                eocd = i;
                break;
            }
        }
        if (eocd < 0)
            return result;

        int cdOffset = bytes[eocd + 16] | (bytes[eocd + 17] << 8) | (bytes[eocd + 18] << 16) | (bytes[eocd + 19] << 24);
        int cdSize = bytes[eocd + 12] | (bytes[eocd + 13] << 8) | (bytes[eocd + 14] << 16) | (bytes[eocd + 15] << 24);

        int pos = cdOffset;
        int end = cdOffset + cdSize;
        while (pos + 46 <= end &&
               bytes[pos] == 0x50 && bytes[pos + 1] == 0x4B && bytes[pos + 2] == 0x01 && bytes[pos + 3] == 0x02)
        {
            ushort flags = (ushort)(bytes[pos + 8] | (bytes[pos + 9] << 8));
            int nameLen = bytes[pos + 28] | (bytes[pos + 29] << 8);
            int extraLen = bytes[pos + 30] | (bytes[pos + 31] << 8);
            int commentLen = bytes[pos + 32] | (bytes[pos + 33] << 8);
            string name = Encoding.UTF8.GetString(bytes, pos + 46, nameLen);
            result[name] = flags;
            pos += 46 + nameLen + extraLen + commentLen;
        }

        return result;
    }

    /// <summary>
    /// Asserts that a zip archive actually uses Zip64 structures: either a Zip64 EOCD locator
    /// (signature PK\x06\x07) sits immediately before the EOCD, or the classic EOCD total-entry
    /// field holds the 0xFFFF sentinel (the real count lives in the Zip64 EOCD). A zip that crosses
    /// a Zip64 boundary but fails to emit these structures would silently truncate entry counts and
    /// offsets (F-24).
    /// </summary>
    private static void AssertZipUsesZip64(string zipPath)
    {
        var bytes = File.ReadAllBytes(zipPath);

        int eocd = -1;
        for (int i = bytes.Length - 22; i >= 0; i--)
        {
            if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x05 && bytes[i + 3] == 0x06)
            {
                eocd = i;
                break;
            }
        }

        Assert.True(eocd >= 0, "EOCD record not found in zip archive.");

        int totalEntries = bytes[eocd + 10] | (bytes[eocd + 11] << 8);
        bool hasZip64Locator = eocd >= 20 &&
            bytes[eocd - 20] == 0x50 && bytes[eocd - 19] == 0x4B &&
            bytes[eocd - 18] == 0x06 && bytes[eocd - 17] == 0x07;

        Assert.True(
            hasZip64Locator || totalEntries == 0xFFFF,
            $"Archive does not use Zip64 structures: EOCD totalEntries=0x{totalEntries:X4}, hasZip64Locator={hasZip64Locator}.");
    }

    #region Multi-Source Zip Compression Tests

    [Fact]
    public async Task Zip_CompressAsync_MultiSourceFiles_PackagesAllFilesPreservingNamesAndContent()
    {
        // Arrange
        var file1 = Path.Combine(_testDir, "zip_alpha.txt");
        var file2 = Path.Combine(_testDir, "zip_beta.json");
        var file3 = Path.Combine(_testDir, "zip_gamma.dat");

        await File.WriteAllTextAsync(file1, "Alpha zip payload");
        await File.WriteAllTextAsync(file2, "{\"zip\":\"beta\"}");
        await File.WriteAllBytesAsync(file3, [10, 20, 30]);

        var zipPath = Path.Combine(_testDir, "multi_files.zip");
        var extractDir = Path.Combine(_testDir, "multi_files_zip_extracted");

        var progressReports = new List<ProgressReport>();
        var progress = new SynchronousProgress<ProgressReport>(progressReports.Add);

        // Act - Compress multi-source
        var request = new ArchiveCompressionRequest([file1, file2, file3], zipPath, 9);
        await _engine.CompressAsync(request, progress);

        // Assert - List entries
        var entries = await _engine.ListEntriesAsync(zipPath);
        Assert.Equal(3, entries.Count(e => !e.IsDirectory));
        Assert.Contains(entries, e => e.RelativePath == "zip_alpha.txt");
        Assert.Contains(entries, e => e.RelativePath == "zip_beta.json");
        Assert.Contains(entries, e => e.RelativePath == "zip_gamma.dat");

        // Act - Extract
        var extractResult = await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));
        Assert.Equal(3, extractResult.FilesExtracted);

        Assert.Equal("Alpha zip payload", await File.ReadAllTextAsync(Path.Combine(extractDir, "zip_alpha.txt")));
        Assert.Equal("{\"zip\":\"beta\"}", await File.ReadAllTextAsync(Path.Combine(extractDir, "zip_beta.json")));
        Assert.Equal(new byte[] { 10, 20, 30 }, await File.ReadAllBytesAsync(Path.Combine(extractDir, "zip_gamma.dat")));
    }

    [Fact]
    public async Task Zip_CompressAsync_MultiSourceDirectoriesAndFiles_PackagesAllPreservingStructure()
    {
        // Arrange
        var dir1 = Path.Combine(_testDir, "zip_folder_a");
        Directory.CreateDirectory(Path.Combine(dir1, "sub"));
        await File.WriteAllTextAsync(Path.Combine(dir1, "doc1.txt"), "Zip Doc 1");
        await File.WriteAllTextAsync(Path.Combine(dir1, "sub", "doc2.txt"), "Zip Doc 2");

        var file1 = Path.Combine(_testDir, "zip_standalone.txt");
        await File.WriteAllTextAsync(file1, "Zip Standalone");

        var zipPath = Path.Combine(_testDir, "multi_dir_file.zip");
        var extractDir = Path.Combine(_testDir, "multi_dir_file_zip_extracted");

        // Act - Compress directory + file with BaseDirectory
        var request = new ArchiveCompressionRequest(["zip_folder_a", "zip_standalone.txt"], zipPath, 9, BaseDirectory: _testDir);
        await _engine.CompressAsync(request);

        // Assert - List entries
        var entries = await _engine.ListEntriesAsync(zipPath);
        Assert.Contains(entries, e => e.RelativePath == "zip_folder_a/doc1.txt");
        Assert.Contains(entries, e => e.RelativePath == "zip_folder_a/sub/doc2.txt");
        Assert.Contains(entries, e => e.RelativePath == "zip_standalone.txt");

        // Act - Extract
        var extractResult = await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));
        Assert.Equal(3, extractResult.FilesExtracted);

        Assert.Equal("Zip Doc 1", await File.ReadAllTextAsync(Path.Combine(extractDir, "zip_folder_a", "doc1.txt")));
        Assert.Equal("Zip Doc 2", await File.ReadAllTextAsync(Path.Combine(extractDir, "zip_folder_a", "sub", "doc2.txt")));
        Assert.Equal("Zip Standalone", await File.ReadAllTextAsync(Path.Combine(extractDir, "zip_standalone.txt")));
    }

    [Fact]
    public async Task Zip_CompressAsync_MultiSource_SanitizesTraversalTokens()
    {
        // Arrange
        var subDir = Path.Combine(_testDir, "zip_sub_work");
        Directory.CreateDirectory(subDir);
        var targetFile = Path.Combine(_testDir, "zip_outside.txt");
        await File.WriteAllTextAsync(targetFile, "Zip Outside content");

        var zipPath = Path.Combine(_testDir, "sanitized_zip_traversal.zip");

        // Path with traversal relative to subDir: "../zip_outside.txt"
        var request = new ArchiveCompressionRequest(["../zip_outside.txt"], zipPath, 9, BaseDirectory: subDir);
        await _engine.CompressAsync(request);

        // Assert - The entry relative path inside the archive must NOT have '..'
        var entries = await _engine.ListEntriesAsync(zipPath);
        var entry = Assert.Single(entries, e => !e.IsDirectory);
        Assert.Equal("zip_outside.txt", entry.RelativePath);
    }

    [Fact]
    public async Task Zip_CompressAsync_MultiSource_NonExistentSource_FailsFastWithoutCreatingTempArchive()
    {
        // Arrange
        var validFile = Path.Combine(_testDir, "zip_valid_file.txt");
        await File.WriteAllTextAsync(validFile, "Valid content");
        var missingFile = Path.Combine(_testDir, "zip_non_existent_file.txt");

        var zipPath = Path.Combine(_testDir, "fail_fast.zip");

        // Act & Assert
        var request = new ArchiveCompressionRequest([validFile, missingFile], zipPath, 9);
        await Assert.ThrowsAsync<FileNotFoundException>(() => _engine.CompressAsync(request));

        // Ensure archive was not created
        Assert.False(File.Exists(zipPath));
        var tempFiles = Directory.GetFiles(_testDir, "fail_fast.zip.tmp.*");
        Assert.Empty(tempFiles);
    }

    #endregion

    #region AppendAsync Tests

    [Fact]
    public async Task Zip_AppendAsync_SingleFile_AppendsToExistingArchive_PreservesExistingAndAddedEntries()
    {
        // Arrange - Create base archive with 2 files
        var initialDir = Path.Combine(_testDir, "zip_append_initial");
        Directory.CreateDirectory(initialDir);
        await File.WriteAllTextAsync(Path.Combine(initialDir, "file1.txt"), "Content of File 1");
        await File.WriteAllTextAsync(Path.Combine(initialDir, "file2.txt"), "Content of File 2");

        var zipPath = Path.Combine(_testDir, "append_test.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(initialDir, zipPath, 9));

        // Create new file to append
        var newFile = Path.Combine(_testDir, "file3.txt");
        await File.WriteAllTextAsync(newFile, "Content of File 3 (Appended)");

        // Act - Append new file
        var appendReq = new ArchiveAppendRequest(zipPath, [newFile], 9);
        var result = await _engine.AppendAsync(appendReq);

        // Assert - Result metrics
        Assert.True(result.Success);
        Assert.Equal("zip", result.Format);
        Assert.Equal(1, result.AddedFiles);
        Assert.Equal(0, result.UpdatedFiles);
        Assert.Equal(2, result.RetainedFiles);
        Assert.Equal(0, result.SkippedFiles);
        Assert.Equal(3, result.TotalFiles);
        Assert.True(result.UncompressedBytes > 0);
        Assert.True(result.CompressedBytes > 0);

        // Act - Extract and verify all 3 files
        var extractDir = Path.Combine(_testDir, "zip_append_extracted");
        var extractResult = await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));
        Assert.Equal(3, extractResult.FilesExtracted);

        Assert.Equal("Content of File 1", await File.ReadAllTextAsync(Path.Combine(extractDir, "file1.txt")));
        Assert.Equal("Content of File 2", await File.ReadAllTextAsync(Path.Combine(extractDir, "file2.txt")));
        Assert.Equal("Content of File 3 (Appended)", await File.ReadAllTextAsync(Path.Combine(extractDir, "file3.txt")));
    }

    [Fact]
    public async Task Zip_AppendAsync_Directory_AppendsSubfolderStructure()
    {
        // Arrange - Create base archive
        var initialDir = Path.Combine(_testDir, "zip_dir_append_initial");
        Directory.CreateDirectory(initialDir);
        await File.WriteAllTextAsync(Path.Combine(initialDir, "root.txt"), "Root content");

        var zipPath = Path.Combine(_testDir, "dir_append.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(initialDir, zipPath, 9));

        // Create extra directory structure to append
        var extraDir = Path.Combine(_testDir, "zip_extra_folder");
        Directory.CreateDirectory(Path.Combine(extraDir, "nested"));
        await File.WriteAllTextAsync(Path.Combine(extraDir, "doc.txt"), "Doc content");
        await File.WriteAllTextAsync(Path.Combine(extraDir, "nested", "inner.txt"), "Inner content");

        // Act
        var appendReq = new ArchiveAppendRequest(zipPath, [extraDir], 9);
        var result = await _engine.AppendAsync(appendReq);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.AddedFiles);
        Assert.Equal(1, result.RetainedFiles);
        Assert.Equal(3, result.TotalFiles);

        var extractDir = Path.Combine(_testDir, "zip_dir_append_extracted");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        Assert.Equal("Root content", await File.ReadAllTextAsync(Path.Combine(extractDir, "root.txt")));
        Assert.Equal("Doc content", await File.ReadAllTextAsync(Path.Combine(extractDir, "zip_extra_folder", "doc.txt")));
        Assert.Equal("Inner content", await File.ReadAllTextAsync(Path.Combine(extractDir, "zip_extra_folder", "nested", "inner.txt")));
    }

    [Fact]
    public async Task Zip_AppendAsync_CollidingEntry_Default_OverwritesExistingContent()
    {
        // Arrange - Create archive with initial content
        var baseDir = Path.Combine(_testDir, "zip_overwrite_base");
        Directory.CreateDirectory(baseDir);
        var baseFile = Path.Combine(baseDir, "data.txt");
        await File.WriteAllTextAsync(baseFile, "Original Version");

        var zipPath = Path.Combine(_testDir, "overwrite_test.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, zipPath, 9));

        // Create updated file with same relative name
        var updatedDir = Path.Combine(_testDir, "zip_overwrite_new");
        Directory.CreateDirectory(updatedDir);
        var updatedFile = Path.Combine(updatedDir, "data.txt");
        await File.WriteAllTextAsync(updatedFile, "Updated Version 2.0");

        // Act - Append without update-only
        var appendReq = new ArchiveAppendRequest(zipPath, ["data.txt"], 9, BaseDirectory: updatedDir);
        var result = await _engine.AppendAsync(appendReq);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.AddedFiles);
        Assert.Equal(1, result.UpdatedFiles);
        Assert.Equal(0, result.RetainedFiles);
        Assert.Equal(0, result.SkippedFiles);
        Assert.Equal(1, result.TotalFiles);

        var extractDir = Path.Combine(_testDir, "zip_overwrite_extracted");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        Assert.Equal("Updated Version 2.0", await File.ReadAllTextAsync(Path.Combine(extractDir, "data.txt")));
    }

    [Fact]
    public async Task Zip_AppendAsync_CollidingEntry_UpdateOnly_WhenSourceNewer_OverwritesExisting()
    {
        // Arrange - Base archive
        var baseDir = Path.Combine(_testDir, "zip_update_newer_base");
        Directory.CreateDirectory(baseDir);
        var baseFile = Path.Combine(baseDir, "log.txt");
        await File.WriteAllTextAsync(baseFile, "Old log");
        var pastTime = DateTime.UtcNow.AddHours(-2);
        File.SetLastWriteTimeUtc(baseFile, pastTime);

        var zipPath = Path.Combine(_testDir, "update_only_newer.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, zipPath, 9));

        // Newer incoming source
        var newDir = Path.Combine(_testDir, "zip_update_newer_in");
        Directory.CreateDirectory(newDir);
        var newFile = Path.Combine(newDir, "log.txt");
        await File.WriteAllTextAsync(newFile, "New log");
        var nowTime = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(newFile, nowTime);

        // Act
        var appendReq = new ArchiveAppendRequest(zipPath, ["log.txt"], 9, UpdateOnly: true, BaseDirectory: newDir);
        var result = await _engine.AppendAsync(appendReq);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.UpdatedFiles);
        Assert.Equal(0, result.RetainedFiles);
        Assert.Equal(0, result.SkippedFiles);
        Assert.Equal(1, result.TotalFiles);

        var extractDir = Path.Combine(_testDir, "zip_update_newer_extracted");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));
        Assert.Equal("New log", await File.ReadAllTextAsync(Path.Combine(extractDir, "log.txt")));
    }

    [Fact]
    public async Task Zip_AppendAsync_CollidingEntry_UpdateOnly_WhenSourceOlderOrSame_RetainsExistingAndSkipsSource()
    {
        // Arrange - Base archive with newer timestamp
        var baseDir = Path.Combine(_testDir, "zip_update_older_base");
        Directory.CreateDirectory(baseDir);
        var baseFile = Path.Combine(baseDir, "config.json");
        await File.WriteAllTextAsync(baseFile, "{\"version\": 2}");
        var newerTime = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(baseFile, newerTime);

        var zipPath = Path.Combine(_testDir, "update_only_older.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, zipPath, 9));

        // Older incoming source
        var oldDir = Path.Combine(_testDir, "zip_update_older_in");
        Directory.CreateDirectory(oldDir);
        var oldFile = Path.Combine(oldDir, "config.json");
        await File.WriteAllTextAsync(oldFile, "{\"version\": 1}");
        var olderTime = DateTime.UtcNow.AddHours(-3);
        File.SetLastWriteTimeUtc(oldFile, olderTime);

        // Act
        var appendReq = new ArchiveAppendRequest(zipPath, ["config.json"], 9, UpdateOnly: true, BaseDirectory: oldDir);
        var result = await _engine.AppendAsync(appendReq);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.AddedFiles);
        Assert.Equal(0, result.UpdatedFiles);
        Assert.Equal(1, result.RetainedFiles);
        Assert.Equal(1, result.SkippedFiles);
        Assert.Equal(1, result.TotalFiles);

        var extractDir = Path.Combine(_testDir, "zip_update_older_extracted");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));
        Assert.Equal("{\"version\": 2}", await File.ReadAllTextAsync(Path.Combine(extractDir, "config.json")));
    }

    [Fact]
    public async Task Zip_AppendAsync_TracksProgressSmoothly_AcrossRetainedAndIncomingEntries()
    {
        // Arrange
        var baseDir = Path.Combine(_testDir, "zip_prog_base");
        Directory.CreateDirectory(baseDir);
        var f1 = Path.Combine(baseDir, "existing.bin");
        var f1Data = new byte[32 * 1024];
        Random.Shared.NextBytes(f1Data);
        await File.WriteAllBytesAsync(f1, f1Data);

        var zipPath = Path.Combine(_testDir, "prog_test.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, zipPath, 9));

        var f2 = Path.Combine(_testDir, "zip_incoming.bin");
        var f2Data = new byte[64 * 1024];
        Random.Shared.NextBytes(f2Data);
        await File.WriteAllBytesAsync(f2, f2Data);

        var progressReports = new List<ProgressReport>();
        var progress = new SynchronousProgress<ProgressReport>(r => progressReports.Add(r));

        // Act
        var appendReq = new ArchiveAppendRequest(zipPath, [f2], 9);
        var result = await _engine.AppendAsync(appendReq, progress);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(progressReports);
        var last = progressReports.Last();
        Assert.Equal(f1Data.Length + f2Data.Length, last.TotalBytes);
        Assert.Equal(last.TotalBytes, last.ProcessedBytes);
        Assert.Equal(100.0, last.Percentage);
    }

    [Fact]
    public async Task Zip_AppendAsync_AtomicRollback_WhenErrorOccurs_PreservesOriginalArchiveAndCleansTempFile()
    {
        // Arrange
        var baseDir = Path.Combine(_testDir, "zip_atomic_base");
        Directory.CreateDirectory(baseDir);
        var baseFile = Path.Combine(baseDir, "original.txt");
        await File.WriteAllTextAsync(baseFile, "Original untouched content");

        var zipPath = Path.Combine(_testDir, "atomic_test.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, zipPath, 9));
        var originalBytes = await File.ReadAllBytesAsync(zipPath);

        // Non-existent source triggers failure upfront
        var missingSource = Path.Combine(_testDir, "does_not_exist_source.txt");

        // Act & Assert
        var appendReq = new ArchiveAppendRequest(zipPath, [missingSource], 9);
        await Assert.ThrowsAsync<FileNotFoundException>(() => _engine.AppendAsync(appendReq));

        // Original archive must be identical and no temp files left
        var currentBytes = await File.ReadAllBytesAsync(zipPath);
        Assert.Equal(originalBytes, currentBytes);

        var tempFiles = Directory.GetFiles(_testDir, "atomic_test.zip.tmp.*");
        Assert.Empty(tempFiles);
    }

    [Fact]
    public async Task Zip_AppendAsync_PreservesPosixPermissions_ForExistingAndNewEntries()
    {
        if (OperatingSystem.IsWindows())
            return;

        // Arrange - Base archive with an executable file
        var baseDir = Path.Combine(_testDir, "zip_posix_base");
        Directory.CreateDirectory(baseDir);
        var execFile = Path.Combine(baseDir, "script.sh");
        await File.WriteAllTextAsync(execFile, "#!/bin/sh\necho Base");
        var execMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                       UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                       UnixFileMode.OtherRead | UnixFileMode.OtherExecute; // 0755
        File.SetUnixFileMode(execFile, execMode);

        var zipPath = Path.Combine(_testDir, "posix_append.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, zipPath, 9));

        // New file to append with 0644 mode
        var regularFile = Path.Combine(_testDir, "readme.txt");
        await File.WriteAllTextAsync(regularFile, "Readme text");
        var regularMode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                          UnixFileMode.GroupRead | UnixFileMode.OtherRead; // 0644
        File.SetUnixFileMode(regularFile, regularMode);

        // Act - Append
        var appendReq = new ArchiveAppendRequest(zipPath, [regularFile], 9);
        var result = await _engine.AppendAsync(appendReq);
        Assert.True(result.Success);

        // Act - Extract and verify POSIX modes
        var extractDir = Path.Combine(_testDir, "zip_posix_extracted");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        var extractedExec = Path.Combine(extractDir, "script.sh");
        var extractedRegular = Path.Combine(extractDir, "readme.txt");

        Assert.True(File.Exists(extractedExec));
        Assert.True(File.Exists(extractedRegular));
        Assert.Equal(execMode, File.GetUnixFileMode(extractedExec));
        Assert.Equal(regularMode, File.GetUnixFileMode(extractedRegular));
    }

    [Fact]
    public async Task Zip_AppendAsync_Level0_Store_CreatesValidArchive()
    {
        // Arrange
        var baseDir = Path.Combine(_testDir, "zip_store_base");
        Directory.CreateDirectory(baseDir);
        var baseFile = Path.Combine(baseDir, "base.txt");
        await File.WriteAllTextAsync(baseFile, "Base store");

        var zipPath = Path.Combine(_testDir, "store_append.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, zipPath, 0));

        var appendFile = Path.Combine(_testDir, "append.txt");
        await File.WriteAllTextAsync(appendFile, "Append store");

        // Act
        var result = await _engine.AppendAsync(new ArchiveAppendRequest(zipPath, [appendFile], 0));
        Assert.True(result.Success);

        // Assert extraction
        var extractDir = Path.Combine(_testDir, "store_append_extracted");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir));

        Assert.Equal("Base store", await File.ReadAllTextAsync(Path.Combine(extractDir, "base.txt")));
        Assert.Equal("Append store", await File.ReadAllTextAsync(Path.Combine(extractDir, "append.txt")));
    }

    [Fact]
    public async Task Zip_AppendAsync_NonExistentArchive_ThrowsFileNotFoundException()
    {
        var missingArchive = Path.Combine(_testDir, "missing.zip");
        var sourceFile = Path.Combine(_testDir, "some_file.txt");
        await File.WriteAllTextAsync(sourceFile, "Some content");

        var req = new ArchiveAppendRequest(missingArchive, [sourceFile], 9);
        await Assert.ThrowsAsync<FileNotFoundException>(() => _engine.AppendAsync(req));
    }

    [Fact]
    public async Task Zip_AppendAsync_InvalidCompressionLevel_ThrowsArgumentOutOfRangeException()
    {
        var baseDir = Path.Combine(_testDir, "zip_lvl_base");
        Directory.CreateDirectory(baseDir);
        var file = Path.Combine(baseDir, "file.txt");
        await File.WriteAllTextAsync(file, "content");

        var zipPath = Path.Combine(_testDir, "level_test.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, zipPath, 9));

        var reqUnder = new ArchiveAppendRequest(zipPath, [file], -1);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _engine.AppendAsync(reqUnder));

        var reqOver = new ArchiveAppendRequest(zipPath, [file], 10);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _engine.AppendAsync(reqOver));
    }

    [Fact]
    public async Task Zip_AppendAsync_EmptySources_ThrowsArgumentException()
    {
        var baseDir = Path.Combine(_testDir, "zip_empty_src_base");
        Directory.CreateDirectory(baseDir);
        var file = Path.Combine(baseDir, "file.txt");
        await File.WriteAllTextAsync(file, "content");

        var zipPath = Path.Combine(_testDir, "empty_src_test.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, zipPath, 9));

        var req = new ArchiveAppendRequest(zipPath, [], 9);
        await Assert.ThrowsAsync<ArgumentException>(() => _engine.AppendAsync(req));
    }

    [Fact]
    public async Task Zip_AppendAsync_UnsupportedFormat_ThrowsNotSupportedException()
    {
        var targzFile = Path.Combine(_testDir, "test.tar.gz");
        await File.WriteAllTextAsync(targzFile, "dummy");
        var sourceFile = Path.Combine(_testDir, "file.txt");
        await File.WriteAllTextAsync(sourceFile, "content");

        var req = new ArchiveAppendRequest(targzFile, [sourceFile], 9);
        await Assert.ThrowsAsync<NotSupportedException>(() => _engine.AppendAsync(req));
    }

    #endregion
}

using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Formats.Tar;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using Xunit;
using ZstdSharp;

namespace RusZip.Core.Tests;

public class ZstdTarArchiveEngineTests : IDisposable
{
    private readonly string _testDir;
    private readonly ZstdTarArchiveEngine _engine;

    public ZstdTarArchiveEngineTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ruszip_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _engine = new ZstdTarArchiveEngine();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { /* Ignore */ }
        }
    }

    [Theory]
    [InlineData(1)]                              // Minimum valid level
    [InlineData(CompressionProfiles.Fast)]      // Fast (3)
    [InlineData(CompressionProfiles.Balanced)]  // Balanced / Default (9)
    [InlineData(CompressionProfiles.High)]      // High (15)
    [InlineData(CompressionProfiles.Ultra)]     // Ultra (22)
    public async Task CompressAndExtract_DirectoryRoundtrip_PreservesStructureAndContents(int compressionLevel)
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "source_" + compressionLevel);
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(Path.Combine(sourceDir, "subfolder"));
        Directory.CreateDirectory(Path.Combine(sourceDir, "empty_subfolder"));

        var file1 = Path.Combine(sourceDir, "hello.txt");
        var file2 = Path.Combine(sourceDir, "subfolder", "nested.txt");
        var file3 = Path.Combine(sourceDir, "binary.dat");

        var binaryData = new byte[64 * 1024]; // 64 KB
        Random.Shared.NextBytes(binaryData);

        await File.WriteAllTextAsync(file1, "Hello World from rus-zip!");
        await File.WriteAllTextAsync(file2, "Nested content in subfolder.");
        await File.WriteAllBytesAsync(file3, binaryData);

        var archivePath = Path.Combine(_testDir, $"output_{compressionLevel}.zrus");
        var extractDir = Path.Combine(_testDir, $"extracted_{compressionLevel}");

        var progressReports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(progressReports.Add);

        // Act - Compress
        var compressReq = new ArchiveCompressionRequest(sourceDir, archivePath, compressionLevel);
        await _engine.CompressAsync(compressReq, progress);

        // Assert - Archive created
        Assert.True(File.Exists(archivePath));
        Assert.True(new FileInfo(archivePath).Length > 0);

        // Act - List
        var entries = await _engine.ListEntriesAsync(archivePath);
        Assert.Contains(entries, e => e.RelativePath.Contains("hello.txt") && !e.IsDirectory);
        Assert.Contains(entries, e => e.RelativePath.Contains("nested.txt") && !e.IsDirectory);
        Assert.Contains(entries, e => e.RelativePath.Contains("binary.dat") && !e.IsDirectory);

        // Act - Extract
        var extractReq = new ArchiveExtractionRequest(archivePath, extractDir);
        await _engine.ExtractAsync(extractReq, progress);

        // Assert - Files extracted properly
        var extractedFile1 = Path.Combine(extractDir, "hello.txt");
        var extractedFile2 = Path.Combine(extractDir, "subfolder", "nested.txt");
        var extractedFile3 = Path.Combine(extractDir, "binary.dat");
        var extractedEmptyDir = Path.Combine(extractDir, "empty_subfolder");

        Assert.True(File.Exists(extractedFile1));
        Assert.True(File.Exists(extractedFile2));
        Assert.True(File.Exists(extractedFile3));
        Assert.True(Directory.Exists(extractedEmptyDir));

        Assert.Equal("Hello World from rus-zip!", await File.ReadAllTextAsync(extractedFile1));
        Assert.Equal("Nested content in subfolder.", await File.ReadAllTextAsync(extractedFile2));
        Assert.Equal(binaryData, await File.ReadAllBytesAsync(extractedFile3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    [InlineData(23)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task Compress_InvalidCompressionLevel_ThrowsArgumentOutOfRangeException(int invalidLevel)
    {
        var dummyFile = Path.Combine(_testDir, "dummy.txt");
        await File.WriteAllTextAsync(dummyFile, "dummy");
        var archivePath = Path.Combine(_testDir, "dummy.zrus");

        var req = new ArchiveCompressionRequest(dummyFile, archivePath, invalidLevel);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _engine.CompressAsync(req));
    }

    [Fact]
    public async Task CompressAndExtract_SingleFile_PreservesContentAndMetadata()
    {
        // Arrange
        var singleFile = Path.Combine(_testDir, "single.txt");
        var content = "Testing single file compression in .zrus format with metadata.";
        await File.WriteAllTextAsync(singleFile, content);

        var expectedTime = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(singleFile, expectedTime);

        var archivePath = Path.Combine(_testDir, "single.zrus");
        var extractDir = Path.Combine(_testDir, "single_extracted");

        // Act - Compress
        await _engine.CompressAsync(new ArchiveCompressionRequest(singleFile, archivePath, CompressionProfiles.Balanced));

        // Act - List
        var entries = await _engine.ListEntriesAsync(archivePath);
        Assert.Single(entries);
        Assert.Equal("single.txt", entries[0].RelativePath);
        Assert.False(entries[0].IsDirectory);
        Assert.Equal(new FileInfo(singleFile).Length, entries[0].UncompressedSize);

        // Act - Extract
        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));

        // Assert
        var extractedFile = Path.Combine(extractDir, "single.txt");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal(content, await File.ReadAllTextAsync(extractedFile));

        var extractedTime = File.GetLastWriteTimeUtc(extractedFile);
        Assert.True(Math.Abs((extractedTime - expectedTime).TotalSeconds) < 2,
            $"Timestamp difference exceeds 2s: expected {expectedTime:O}, got {extractedTime:O}");
    }

    [Fact]
    public async Task CompressAndExtract_PreservesModificationTimestamps_ForFilesAndDirectories()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "timestamp_source");
        var subDir = Path.Combine(sourceDir, "sub_dir");
        Directory.CreateDirectory(subDir);

        var file1 = Path.Combine(sourceDir, "file1.txt");
        var file2 = Path.Combine(subDir, "file2.txt");

        await File.WriteAllTextAsync(file1, "file1 content");
        await File.WriteAllTextAsync(file2, "file2 content");

        var file1Time = new DateTime(2023, 3, 10, 14, 20, 0, DateTimeKind.Utc);
        var file2Time = new DateTime(2022, 11, 5, 8, 45, 0, DateTimeKind.Utc);
        var dirTime = new DateTime(2021, 8, 20, 12, 0, 0, DateTimeKind.Utc);

        File.SetLastWriteTimeUtc(file1, file1Time);
        File.SetLastWriteTimeUtc(file2, file2Time);
        Directory.SetLastWriteTimeUtc(subDir, dirTime);

        var archivePath = Path.Combine(_testDir, "timestamp.zrus");
        var extractDir = Path.Combine(_testDir, "timestamp_extracted");

        // Act
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 9));
        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));

        // Assert
        var extractedFile1 = Path.Combine(extractDir, "file1.txt");
        var extractedFile2 = Path.Combine(extractDir, "sub_dir", "file2.txt");
        var extractedSubDir = Path.Combine(extractDir, "sub_dir");

        Assert.True(Math.Abs((File.GetLastWriteTimeUtc(extractedFile1) - file1Time).TotalSeconds) < 2);
        Assert.True(Math.Abs((File.GetLastWriteTimeUtc(extractedFile2) - file2Time).TotalSeconds) < 2);
        Assert.True(Math.Abs((Directory.GetLastWriteTimeUtc(extractedSubDir) - dirTime).TotalSeconds) < 2);
    }

    [Fact]
    public async Task CompressAndExtract_PreservesPosixPermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            // UnixFileMode is not supported on Windows
            return;
        }

        // Arrange
        var sourceDir = Path.Combine(_testDir, "posix_source");
        Directory.CreateDirectory(sourceDir);

        var execFile = Path.Combine(sourceDir, "run.sh");
        var readOnlyFile = Path.Combine(sourceDir, "readonly.txt");
        var secretFile = Path.Combine(sourceDir, "secret.key");

        await File.WriteAllTextAsync(execFile, "#!/bin/sh\necho Hello");
        await File.WriteAllTextAsync(readOnlyFile, "read only data");
        await File.WriteAllTextAsync(secretFile, "super secret");

        // Set POSIX modes:
        // 0755: rwxr-xr-x
        var execMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                       UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                       UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

        // 0444: r--r--r--
        var readOnlyMode = UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

        // 0600: rw-------
        var secretMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        File.SetUnixFileMode(execFile, execMode);
        File.SetUnixFileMode(readOnlyFile, readOnlyMode);
        File.SetUnixFileMode(secretFile, secretMode);

        var archivePath = Path.Combine(_testDir, "posix.zrus");
        var extractDir = Path.Combine(_testDir, "posix_extracted");

        // Act
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 9));
        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));

        // Assert
        var extractedExec = Path.Combine(extractDir, "run.sh");
        var extractedReadOnly = Path.Combine(extractDir, "readonly.txt");
        var extractedSecret = Path.Combine(extractDir, "secret.key");

        Assert.Equal(execMode, File.GetUnixFileMode(extractedExec));
        Assert.Equal(readOnlyMode, File.GetUnixFileMode(extractedReadOnly));
        Assert.Equal(secretMode, File.GetUnixFileMode(extractedSecret));
    }

    [Theory]
    [InlineData("../../../evil.txt")]
    [InlineData("../sibling/evil.txt")]
    [InlineData("folder/../../../evil.txt")]
    [InlineData("/tmp/absolute_evil.txt")]
    [InlineData(@"..\..\windows_evil.txt")]
    public async Task Extract_TarSlip_PathTraversal_ThrowsSecurityException(string maliciousPath)
    {
        // Arrange - Build synthetic malicious archive
        var archivePath = Path.Combine(_testDir, $"malicious_{Guid.NewGuid():N}.zrus");
        var extractDir = Path.Combine(_testDir, $"extract_{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);

        await using (var fs = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write))
        await using (var zstdStream = new CompressionStream(fs, 3))
        await using (var tarWriter = new TarWriter(zstdStream, TarEntryFormat.Pax, leaveOpen: false))
        {
            var contentBytes = Encoding.UTF8.GetBytes("malicious exploit payload");
            using var ms = new MemoryStream(contentBytes);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, maliciousPath)
            {
                DataStream = ms
            };
            await tarWriter.WriteEntryAsync(entry);
        }

        // Act & Assert
        var req = new ArchiveExtractionRequest(archivePath, extractDir);
        var ex = await Assert.ThrowsAsync<SecurityException>(() => _engine.ExtractAsync(req));
        Assert.Contains("Malicious path traversal detected", ex.Message);
    }

    [Fact]
    public async Task Extract_CorruptedArchive_ThrowsArchiveIntegrityException()
    {
        var corruptedFile = Path.Combine(_testDir, "corrupted.zrus");
        await File.WriteAllBytesAsync(corruptedFile, [0x28, 0xB5, 0x2F, 0xFD, 0x00, 0x11, 0x22, 0x33, 0x44]);

        var extractDir = Path.Combine(_testDir, "corrupted_extracted");
        await Assert.ThrowsAnyAsync<ArchiveIntegrityException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(corruptedFile, extractDir)));
    }

    [Fact]
    public async Task ListEntries_CorruptedArchive_ThrowsArchiveIntegrityException()
    {
        var corruptedFile = Path.Combine(_testDir, "corrupted_list.zrus");
        await File.WriteAllBytesAsync(corruptedFile, [0x28, 0xB5, 0x2F, 0xFD, 0xDE, 0xAD, 0xBE, 0xEF]);

        await Assert.ThrowsAsync<ArchiveIntegrityException>(() =>
            _engine.ListEntriesAsync(corruptedFile));
    }

    [Fact]
    public async Task Extract_MidStreamCorruption_ThrowsArchiveIntegrityException_AndCleansUpPartialFiles()
    {
        // Arrange: a large incompressible payload ensures the flipped byte lands in raw-block data
        // (valid decompression with different content) rather than a tar header — reproducing F-08,
        // where a mid-stream flip previously extracted silently with exit 0.
        var sourceDir = Path.Combine(_testDir, "midstream_src");
        Directory.CreateDirectory(sourceDir);
        var payloadPath = Path.Combine(sourceDir, "payload.bin");
        var payload = new byte[5 * 1024 * 1024]; // 5 MB
        new Random(12345).NextBytes(payload);
        await File.WriteAllBytesAsync(payloadPath, payload);

        var archivePath = Path.Combine(_testDir, "midstream.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 3));

        var corruptedPath = Path.Combine(_testDir, "midstream_bad.zrus");
        var bytes = await File.ReadAllBytesAsync(archivePath);
        bytes[bytes.Length / 2] ^= 0xFF;
        await File.WriteAllBytesAsync(corruptedPath, bytes);

        var extractDir = Path.Combine(_testDir, "midstream_out");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArchiveIntegrityException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(corruptedPath, extractDir)));

        Assert.Contains("checksum", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("payload.bin", ex.EntryName);
        // The partial (corrupt) output must be cleaned up.
        Assert.False(File.Exists(Path.Combine(extractDir, "payload.bin")));
        Assert.Empty(Directory.GetFiles(extractDir));
    }

    [Fact]
    public async Task ListEntries_MidStreamCorruption_ThrowsArchiveIntegrityException()
    {
        // Same corruption as the extraction test: `list` must fail on a checksum-broken archive
        // (DoD #1), never report a silent success-shaped empty list.
        var sourceDir = Path.Combine(_testDir, "midstream_list_src");
        Directory.CreateDirectory(sourceDir);
        var payloadPath = Path.Combine(sourceDir, "payload.bin");
        var payload = new byte[5 * 1024 * 1024];
        new Random(67890).NextBytes(payload);
        await File.WriteAllBytesAsync(payloadPath, payload);

        var archivePath = Path.Combine(_testDir, "midstream_list.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 3));

        var corruptedPath = Path.Combine(_testDir, "midstream_list_bad.zrus");
        var bytes = await File.ReadAllBytesAsync(archivePath);
        bytes[bytes.Length / 2] ^= 0xFF;
        await File.WriteAllBytesAsync(corruptedPath, bytes);

        var ex = await Assert.ThrowsAsync<ArchiveIntegrityException>(() =>
            _engine.ListEntriesAsync(corruptedPath));

        Assert.Contains("checksum", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Compress_NonExistentSource_ThrowsFileNotFoundException()
    {
        var nonExistent = Path.Combine(_testDir, "does_not_exist_" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(_testDir, "output.zrus");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _engine.CompressAsync(new ArchiveCompressionRequest(nonExistent, archivePath, 9)));
    }

    [Fact]
    public async Task Extract_NonExistentArchive_ThrowsFileNotFoundException()
    {
        var nonExistent = Path.Combine(_testDir, "missing.zrus");
        var extractDir = Path.Combine(_testDir, "extract");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(nonExistent, extractDir)));
    }

    [Fact]
    public async Task Extract_StreamingExtraction_MaintainsO1MemoryUsageAndVerifiesChecksum()
    {
        // Arrange - Create a 5 MB data file with deterministic pattern
        var sourceDir = Path.Combine(_testDir, "streaming_source");
        Directory.CreateDirectory(sourceDir);

        var largeFilePath = Path.Combine(sourceDir, "large_payload.bin");
        const int payloadSize = 5 * 1024 * 1024; // 5 MB
        var pattern = new byte[8192];
        for (int i = 0; i < pattern.Length; i++)
        {
            pattern[i] = (byte)(i % 251);
        }

        await using (var fs = new FileStream(largeFilePath, FileMode.Create, FileAccess.Write))
        {
            for (int written = 0; written < payloadSize; written += pattern.Length)
            {
                await fs.WriteAsync(pattern.AsMemory(0, Math.Min(pattern.Length, payloadSize - written)));
            }
        }

        byte[] originalHash;
        using (var sha = SHA256.Create())
        await using (var fs = File.OpenRead(largeFilePath))
        {
            originalHash = await sha.ComputeHashAsync(fs);
        }

        var archivePath = Path.Combine(_testDir, "streaming.zrus");
        var extractDir = Path.Combine(_testDir, "streaming_extracted");

        // Act - Compress
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 3));

        // Act - Extract with progress tracking
        var progressReports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(r => progressReports.Add(r));

        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir), progress);

        // Assert - Data integrity
        var extractedLargeFile = Path.Combine(extractDir, "large_payload.bin");
        Assert.True(File.Exists(extractedLargeFile));
        Assert.Equal(payloadSize, new FileInfo(extractedLargeFile).Length);

        byte[] extractedHash;
        using (var sha = SHA256.Create())
        await using (var fs = File.OpenRead(extractedLargeFile))
        {
            extractedHash = await sha.ComputeHashAsync(fs);
        }

        Assert.Equal(originalHash, extractedHash);
        Assert.NotEmpty(progressReports);
        Assert.Equal(payloadSize, progressReports.Last().ProcessedBytes);
    }

    [Fact]
    public async Task Extract_OverwriteFalse_WhenFileAlreadyExists_ThrowsIOException()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "overwrite_source");
        Directory.CreateDirectory(sourceDir);
        var file = Path.Combine(sourceDir, "existing.txt");
        await File.WriteAllTextAsync(file, "Original text");

        var archivePath = Path.Combine(_testDir, "overwrite.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 3));

        var extractDir = Path.Combine(_testDir, "overwrite_extracted");
        Directory.CreateDirectory(extractDir);
        var targetFile = Path.Combine(extractDir, "existing.txt");
        await File.WriteAllTextAsync(targetFile, "Pre-existing file content");

        // Act & Assert
        var req = new ArchiveExtractionRequest(archivePath, extractDir, Overwrite: false);
        await Assert.ThrowsAsync<IOException>(() => _engine.ExtractAsync(req));
    }

    [Fact]
    public async Task Compress_CancellationToken_AbortsOperation()
    {
        var sourceDir = Path.Combine(_testDir, "cancel_source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "hello");

        var archivePath = Path.Combine(_testDir, "cancel.zrus");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 3), ct: cts.Token));
    }

    [Fact]
    public async Task Extract_CancellationToken_AbortsOperation()
    {
        var sourceDir = Path.Combine(_testDir, "cancel_extract_src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        var archivePath = Path.Combine(_testDir, "cancel_extract.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 3));

        var extractDir = Path.Combine(_testDir, "cancel_extract_out");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir), ct: cts.Token));
    }

    [Fact]
    public async Task ListEntries_CancellationToken_AbortsOperation()
    {
        var sourceDir = Path.Combine(_testDir, "cancel_list_src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "content");

        var archivePath = Path.Combine(_testDir, "cancel_list.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 3));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _engine.ListEntriesAsync(archivePath, ct: cts.Token));
    }

    [Fact]
    public async Task CompressAndExtract_UnicodeAndSpecialCharacters_RoundtripsSuccessfully()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "unicode_source");
        Directory.CreateDirectory(sourceDir);

        var subDir = Path.Combine(sourceDir, "📁 測試 目錄");
        Directory.CreateDirectory(subDir);

        var unicodeFile = Path.Combine(subDir, "файл-тест_日本語_🎉.txt");
        var content = "Unicode content: 🚀 壓縮 và Giải nén 100% OK!";
        await File.WriteAllTextAsync(unicodeFile, content);

        var archivePath = Path.Combine(_testDir, "unicode.zrus");
        var extractDir = Path.Combine(_testDir, "unicode_extracted");

        // Act
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 9));
        var entries = await _engine.ListEntriesAsync(archivePath);

        Assert.Contains(entries, e => e.RelativePath.Contains("файл-тест_日本語_🎉.txt"));

        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));

        // Assert
        var extractedFile = Path.Combine(extractDir, "📁 測試 目錄", "файл-тест_日本語_🎉.txt");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal(content, await File.ReadAllTextAsync(extractedFile));
    }

    [Fact]
    public async Task CompressAndExtract_EmptyFilesAndDeepHierarchy_Preserved()
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "deep_source");
        var deepPath = Path.Combine(sourceDir, "level1", "level2", "level3");
        Directory.CreateDirectory(deepPath);

        var emptyFile = Path.Combine(deepPath, "empty.txt");
        await File.WriteAllTextAsync(emptyFile, string.Empty);

        var normalFile = Path.Combine(deepPath, "normal.txt");
        await File.WriteAllTextAsync(normalFile, "some text");

        var archivePath = Path.Combine(_testDir, "deep.zrus");
        var extractDir = Path.Combine(_testDir, "deep_extracted");

        // Act
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 9));
        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));

        // Assert
        var extractedEmpty = Path.Combine(extractDir, "level1", "level2", "level3", "empty.txt");
        var extractedNormal = Path.Combine(extractDir, "level1", "level2", "level3", "normal.txt");

        Assert.True(File.Exists(extractedEmpty));
        Assert.Equal(0, new FileInfo(extractedEmpty).Length);
        Assert.True(File.Exists(extractedNormal));
        Assert.Equal("some text", await File.ReadAllTextAsync(extractedNormal));
    }

    [Fact]
    public async Task Extract_TarSlip_DirectoryTraversal_ThrowsSecurityException()
    {
        // Arrange - Build synthetic archive with malicious directory entry
        var archivePath = Path.Combine(_testDir, $"malicious_dir_{Guid.NewGuid():N}.zrus");
        var extractDir = Path.Combine(_testDir, $"extract_dir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);

        await using (var fs = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write))
        await using (var zstdStream = new CompressionStream(fs, 3))
        await using (var tarWriter = new TarWriter(zstdStream, TarEntryFormat.Pax, leaveOpen: false))
        {
            var dirEntry = new PaxTarEntry(TarEntryType.Directory, "../../../evil_dir/");
            await tarWriter.WriteEntryAsync(dirEntry);
        }

        // Act & Assert
        var req = new ArchiveExtractionRequest(archivePath, extractDir);
        var ex = await Assert.ThrowsAsync<SecurityException>(() => _engine.ExtractAsync(req));
        Assert.Contains("Malicious path traversal detected", ex.Message);
    }
}

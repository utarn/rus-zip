using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Formats.Tar;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using Xunit;
using ZstdSharp;
using ZstdSharp.Unsafe;

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

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
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
        var progress = new SynchronousProgress<ProgressReport>(r => progressReports.Add(r));

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

    [Fact]
    public async Task Compress_EmptyDirectory_ProducesValidEmptyTarZstd_RoundtripsViaListAndExtract()
    {
        // F-11: an empty directory previously produced a 13-byte zstd frame with empty content that
        // tar readers could not parse. A valid empty tar is exactly two 512-byte zero blocks.
        var sourceDir = Path.Combine(_testDir, "empty_dir_src");
        Directory.CreateDirectory(sourceDir);

        var archivePath = Path.Combine(_testDir, "empty_dir.zrus");
        var extractDir = Path.Combine(_testDir, "empty_dir_out");

        // Act — compress
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, CompressionProfiles.Balanced));
        Assert.True(File.Exists(archivePath));

        // The decompressed payload must be exactly the two 512-byte zero blocks (a valid empty tar).
        byte[] decompressed;
        await using (var fs = File.OpenRead(archivePath))
        await using (var ds = new DecompressionStream(fs))
        using (var ms = new MemoryStream())
        {
            await ds.CopyToAsync(ms);
            decompressed = ms.ToArray();
        }
        Assert.Equal(1024, decompressed.Length);
        Assert.All(decompressed, b => Assert.Equal(0, b));

        // Act — list
        var entries = await _engine.ListEntriesAsync(archivePath);
        Assert.Empty(entries);

        // Act — extract
        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));

        // Assert — destination exists and is empty
        Assert.True(Directory.Exists(extractDir));
        Assert.Empty(Directory.GetFileSystemEntries(extractDir));
    }

    [Fact]
    public async Task ListAndExtract_LegacyEmptyZrusFrame_TreatAsEmptyArchive()
    {
        // F-11 read-side compat: the pre-fix empty-directory output is a *valid* zstd frame with
        // zero decompressed bytes (no tar end-of-archive blocks). It is unambiguous (no entries), so
        // treat it as an empty archive instead of surfacing a confusing "end of stream" integrity error.
        var archivePath = Path.Combine(_testDir, "legacy_empty.zrus");
        await using (var fs = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write))
        await using (var cs = new CompressionStream(fs, 3))
        {
            cs.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
            // Write nothing — produces the legacy 13-byte empty frame.
        }
        Assert.Equal(13, new FileInfo(archivePath).Length);

        // Act — list
        var entries = await _engine.ListEntriesAsync(archivePath);
        Assert.Empty(entries);

        // Act — extract
        var extractDir = Path.Combine(_testDir, "legacy_empty_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));

        // Assert
        Assert.True(Directory.Exists(extractDir));
        Assert.Empty(Directory.GetFileSystemEntries(extractDir));
        Assert.Equal(0, result.FilesExtracted);
    }

    [Fact]
    public async Task CompressAndExtract_NonEmptyDirectory_RoundtripUnchanged_AndEndsWithTwoZeroBlocks()
    {
        // Regression for F-11: the write-path change (leaveOpen + explicit zero blocks for empty
        // archives) must not alter non-empty round-trips. A non-empty tar must still end with the
        // two 512-byte zero end-of-archive blocks written by TarWriter.
        var sourceDir = Path.Combine(_testDir, "nonempty_regression_src");
        var subDir = Path.Combine(sourceDir, "sub");
        Directory.CreateDirectory(subDir);
        var file1 = Path.Combine(sourceDir, "a.txt");
        var file2 = Path.Combine(subDir, "b.txt");
        await File.WriteAllTextAsync(file1, "alpha");
        await File.WriteAllTextAsync(file2, "beta");

        var archivePath = Path.Combine(_testDir, "nonempty_regression.zrus");
        var extractDir = Path.Combine(_testDir, "nonempty_regression_out");

        // Act — compress
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, CompressionProfiles.Balanced));

        // The decompressed tar must end with two 512-byte zero blocks.
        byte[] decompressed;
        await using (var fs = File.OpenRead(archivePath))
        await using (var ds = new DecompressionStream(fs))
        using (var ms = new MemoryStream())
        {
            await ds.CopyToAsync(ms);
            decompressed = ms.ToArray();
        }
        Assert.True(decompressed.Length >= 1024);
        Assert.All(decompressed.Skip(decompressed.Length - 1024), b => Assert.Equal(0, b));

        // Act — list & extract
        var entries = await _engine.ListEntriesAsync(archivePath);
        Assert.Contains(entries, e => e.RelativePath.EndsWith("a.txt"));
        Assert.Contains(entries, e => e.RelativePath.EndsWith("b.txt"));

        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));
        Assert.Equal("alpha", await File.ReadAllTextAsync(Path.Combine(extractDir, "a.txt")));
        Assert.Equal("beta", await File.ReadAllTextAsync(Path.Combine(extractDir, "sub", "b.txt")));
    }

    #region Selective extraction (entry filter)

    [Fact]
    public async Task Extract_WithSingleFileFilter_ExtractsOnlyThatFile()
    {
        var sourceDir = Path.Combine(_testDir, "filter_single_src");
        Directory.CreateDirectory(Path.Combine(sourceDir, "folder"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "b.txt"), "beta");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "folder", "c.txt"), "gamma");

        var archivePath = Path.Combine(_testDir, "filter_single.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 3));

        var extractDir = Path.Combine(_testDir, "filter_single_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir, Entries: ["b.txt"]));

        Assert.Equal(1, result.FilesExtracted);
        Assert.Equal("beta", await File.ReadAllTextAsync(Path.Combine(extractDir, "b.txt")));
        Assert.False(File.Exists(Path.Combine(extractDir, "a.txt")));
        Assert.False(Directory.Exists(Path.Combine(extractDir, "folder")));
    }

    [Fact]
    public async Task Extract_WithFolderSubtreeFilter_ExtractsSubtreeOnly()
    {
        var sourceDir = Path.Combine(_testDir, "filter_folder_src");
        Directory.CreateDirectory(Path.Combine(sourceDir, "sub", "deep"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "root.txt"), "root");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "sub", "one.txt"), "one");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "sub", "deep", "two.txt"), "two");

        var archivePath = Path.Combine(_testDir, "filter_folder.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 3));

        var extractDir = Path.Combine(_testDir, "filter_folder_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir, Entries: ["sub"]));

        Assert.Equal(2, result.FilesExtracted);
        Assert.True(File.Exists(Path.Combine(extractDir, "sub", "one.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "sub", "deep", "two.txt")));
        Assert.False(File.Exists(Path.Combine(extractDir, "root.txt")));
    }

    [Fact]
    public async Task Extract_WithFolderFilter_TrailingSlash_MatchesSameSubtree()
    {
        var sourceDir = Path.Combine(_testDir, "filter_folder_slash_src");
        Directory.CreateDirectory(Path.Combine(sourceDir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "sub", "one.txt"), "one");

        var archivePath = Path.Combine(_testDir, "filter_folder_slash.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 3));

        var extractDir = Path.Combine(_testDir, "filter_folder_slash_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir, Entries: ["sub/"]));

        Assert.Equal(1, result.FilesExtracted);
        Assert.True(File.Exists(Path.Combine(extractDir, "sub", "one.txt")));
    }

    [Fact]
    public async Task Extract_WithNoMatchFilter_ThrowsInvalidOperationException()
    {
        var sourceDir = Path.Combine(_testDir, "filter_nomatch_src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "alpha");

        var archivePath = Path.Combine(_testDir, "filter_nomatch.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 3));

        var extractDir = Path.Combine(_testDir, "filter_nomatch_out");
        var req = new ArchiveExtractionRequest(archivePath, extractDir, Entries: ["nonexistent.txt"]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _engine.ExtractAsync(req));
        Assert.Contains("No archive entries matched", ex.Message);
        // Nothing should have been written.
        Assert.True(Directory.Exists(extractDir));
        Assert.Empty(Directory.GetFileSystemEntries(extractDir));
    }

    [Fact]
    public async Task Extract_FilterWithTraversalName_StillRefused()
    {
        var archivePath = Path.Combine(_testDir, $"filter_slip_{Guid.NewGuid():N}.zrus");
        var extractDir = Path.Combine(_testDir, $"filter_slip_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);

        await using (var fs = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write))
        await using (var zstdStream = new CompressionStream(fs, 3))
        await using (var tarWriter = new TarWriter(zstdStream, TarEntryFormat.Pax, leaveOpen: false))
        {
            var contentBytes = Encoding.UTF8.GetBytes("malicious");
            using var ms = new MemoryStream(contentBytes);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "../../evil.txt")
            {
                DataStream = ms
            };
            await tarWriter.WriteEntryAsync(entry);
        }

        // The filter matches the malicious entry, but SafeArchiveExtractor must still refuse it.
        var req = new ArchiveExtractionRequest(archivePath, extractDir, Entries: ["../../evil.txt"]);
        var ex = await Assert.ThrowsAsync<SecurityException>(() => _engine.ExtractAsync(req));
        Assert.Contains("Malicious path traversal detected", ex.Message);
    }

    [Fact]
    public async Task Extract_FilterExcludingMaliciousEntry_SkipsItAndSucceeds()
    {
        var archivePath = Path.Combine(_testDir, $"filter_skip_slip_{Guid.NewGuid():N}.zrus");
        var extractDir = Path.Combine(_testDir, $"filter_skip_slip_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);

        await using (var fs = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write))
        await using (var zstdStream = new CompressionStream(fs, 3))
        await using (var tarWriter = new TarWriter(zstdStream, TarEntryFormat.Pax, leaveOpen: false))
        {
            var good = new PaxTarEntry(TarEntryType.RegularFile, "good.txt")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("good"))
            };
            await tarWriter.WriteEntryAsync(good);

            var evil = new PaxTarEntry(TarEntryType.RegularFile, "../../evil.txt")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("bad"))
            };
            await tarWriter.WriteEntryAsync(evil);
        }

        // The malicious entry is outside the filter, so it is skipped (never validated, never written).
        var req = new ArchiveExtractionRequest(archivePath, extractDir, Entries: ["good.txt"]);
        var result = await _engine.ExtractAsync(req);

        Assert.Equal(1, result.FilesExtracted);
        Assert.True(File.Exists(Path.Combine(extractDir, "good.txt")));
        Assert.False(File.Exists(Path.Combine(extractDir, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "evil.txt")));
    }

    [Fact]
    public async Task Extract_NullFilter_ExtractsAllEntries()
    {
        var sourceDir = Path.Combine(_testDir, "filter_null_src");
        Directory.CreateDirectory(Path.Combine(sourceDir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "sub", "b.txt"), "beta");

        var archivePath = Path.Combine(_testDir, "filter_null.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 3));

        var extractDir = Path.Combine(_testDir, "filter_null_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir, Entries: null));

        Assert.Equal(2, result.FilesExtracted);
        Assert.True(File.Exists(Path.Combine(extractDir, "a.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "sub", "b.txt")));
    }

    #endregion

    #region Multi-Source Compression Tests

    [Fact]
    public async Task CompressAsync_MultiSourceFiles_PackagesAllFilesPreservingNamesAndContent()
    {
        // Arrange
        var file1 = Path.Combine(_testDir, "alpha.txt");
        var file2 = Path.Combine(_testDir, "beta.json");
        var file3 = Path.Combine(_testDir, "gamma.dat");

        await File.WriteAllTextAsync(file1, "Alpha payload");
        await File.WriteAllTextAsync(file2, "{\"key\":\"beta\"}");
        await File.WriteAllBytesAsync(file3, [1, 2, 3, 4, 5]);

        var archivePath = Path.Combine(_testDir, "multi_files.zrus");
        var extractDir = Path.Combine(_testDir, "multi_files_extracted");

        var progressReports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(progressReports.Add);

        // Act - Compress multi-source
        var request = new ArchiveCompressionRequest([file1, file2, file3], archivePath, 9);
        await _engine.CompressAsync(request, progress);

        // Assert - List entries
        var entries = await _engine.ListEntriesAsync(archivePath);
        Assert.Equal(3, entries.Count(e => !e.IsDirectory));
        Assert.Contains(entries, e => e.RelativePath == "alpha.txt");
        Assert.Contains(entries, e => e.RelativePath == "beta.json");
        Assert.Contains(entries, e => e.RelativePath == "gamma.dat");

        // Act - Extract
        var extractResult = await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));
        Assert.Equal(3, extractResult.FilesExtracted);

        Assert.Equal("Alpha payload", await File.ReadAllTextAsync(Path.Combine(extractDir, "alpha.txt")));
        Assert.Equal("{\"key\":\"beta\"}", await File.ReadAllTextAsync(Path.Combine(extractDir, "beta.json")));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, await File.ReadAllBytesAsync(Path.Combine(extractDir, "gamma.dat")));
    }

    [Fact]
    public async Task CompressAsync_MultiSourceDirectoriesAndFiles_PackagesAllPreservingStructure()
    {
        // Arrange
        var dir1 = Path.Combine(_testDir, "folder_a");
        Directory.CreateDirectory(Path.Combine(dir1, "sub"));
        await File.WriteAllTextAsync(Path.Combine(dir1, "doc1.txt"), "Doc 1");
        await File.WriteAllTextAsync(Path.Combine(dir1, "sub", "doc2.txt"), "Doc 2");

        var file1 = Path.Combine(_testDir, "standalone.txt");
        await File.WriteAllTextAsync(file1, "Standalone");

        var archivePath = Path.Combine(_testDir, "multi_dir_file.zrus");
        var extractDir = Path.Combine(_testDir, "multi_dir_file_extracted");

        // Act - Compress directory + file with BaseDirectory
        var request = new ArchiveCompressionRequest(["folder_a", "standalone.txt"], archivePath, 9, BaseDirectory: _testDir);
        await _engine.CompressAsync(request);

        // Assert - List entries
        var entries = await _engine.ListEntriesAsync(archivePath);
        Assert.Contains(entries, e => e.RelativePath == "folder_a/doc1.txt");
        Assert.Contains(entries, e => e.RelativePath == "folder_a/sub/doc2.txt");
        Assert.Contains(entries, e => e.RelativePath == "standalone.txt");

        // Act - Extract
        var extractResult = await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));
        Assert.Equal(3, extractResult.FilesExtracted);

        Assert.Equal("Doc 1", await File.ReadAllTextAsync(Path.Combine(extractDir, "folder_a", "doc1.txt")));
        Assert.Equal("Doc 2", await File.ReadAllTextAsync(Path.Combine(extractDir, "folder_a", "sub", "doc2.txt")));
        Assert.Equal("Standalone", await File.ReadAllTextAsync(Path.Combine(extractDir, "standalone.txt")));
    }

    [Fact]
    public async Task CompressAsync_MultiSource_SanitizesTraversalTokens()
    {
        // Arrange
        var subDir = Path.Combine(_testDir, "sub_work");
        Directory.CreateDirectory(subDir);
        var targetFile = Path.Combine(_testDir, "outside.txt");
        await File.WriteAllTextAsync(targetFile, "Outside content");

        var archivePath = Path.Combine(_testDir, "sanitized_traversal.zrus");

        // Path with traversal relative to subDir: "../outside.txt"
        var request = new ArchiveCompressionRequest(["../outside.txt"], archivePath, 9, BaseDirectory: subDir);
        await _engine.CompressAsync(request);

        // Assert - The entry relative path inside the archive must NOT have '..'
        var entries = await _engine.ListEntriesAsync(archivePath);
        var entry = Assert.Single(entries, e => !e.IsDirectory);
        Assert.Equal("outside.txt", entry.RelativePath);
    }

    [Fact]
    public async Task CompressAsync_MultiSource_NonExistentSource_FailsFastWithoutCreatingTempArchive()
    {
        // Arrange
        var validFile = Path.Combine(_testDir, "valid_file.txt");
        await File.WriteAllTextAsync(validFile, "Valid content");
        var missingFile = Path.Combine(_testDir, "non_existent_file.txt");

        var archivePath = Path.Combine(_testDir, "fail_fast.zrus");

        // Act & Assert
        var request = new ArchiveCompressionRequest([validFile, missingFile], archivePath, 9);
        await Assert.ThrowsAsync<FileNotFoundException>(() => _engine.CompressAsync(request));

        // Ensure archive was not created
        Assert.False(File.Exists(archivePath));
        var tempFiles = Directory.GetFiles(_testDir, "fail_fast.zrus.tmp.*");
        Assert.Empty(tempFiles);
    }

    #endregion

    #region AppendAsync Tests

    [Fact]
    public async Task AppendAsync_SingleFile_AppendsToExistingArchive_PreservesExistingAndAddedEntries()
    {
        // Arrange - Create base archive with 2 files
        var initialDir = Path.Combine(_testDir, "append_initial");
        Directory.CreateDirectory(initialDir);
        await File.WriteAllTextAsync(Path.Combine(initialDir, "file1.txt"), "Content of File 1");
        await File.WriteAllTextAsync(Path.Combine(initialDir, "file2.txt"), "Content of File 2");

        var archivePath = Path.Combine(_testDir, "append_test.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(initialDir, archivePath, 9));

        // Create new file to append
        var newFile = Path.Combine(_testDir, "file3.txt");
        await File.WriteAllTextAsync(newFile, "Content of File 3 (Appended)");

        // Act - Append new file
        var appendReq = new ArchiveAppendRequest(archivePath, [newFile], 9);
        var result = await _engine.AppendAsync(appendReq);

        // Assert - Result metrics
        Assert.True(result.Success);
        Assert.Equal("zrus", result.Format);
        Assert.Equal(1, result.AddedFiles);
        Assert.Equal(0, result.UpdatedFiles);
        Assert.Equal(2, result.RetainedFiles);
        Assert.Equal(0, result.SkippedFiles);
        Assert.Equal(3, result.TotalFiles);
        Assert.True(result.UncompressedBytes > 0);
        Assert.True(result.CompressedBytes > 0);

        // Act - Extract and verify all 3 files
        var extractDir = Path.Combine(_testDir, "append_extracted");
        var extractResult = await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));
        Assert.Equal(3, extractResult.FilesExtracted);

        Assert.Equal("Content of File 1", await File.ReadAllTextAsync(Path.Combine(extractDir, "file1.txt")));
        Assert.Equal("Content of File 2", await File.ReadAllTextAsync(Path.Combine(extractDir, "file2.txt")));
        Assert.Equal("Content of File 3 (Appended)", await File.ReadAllTextAsync(Path.Combine(extractDir, "file3.txt")));
    }

    [Fact]
    public async Task AppendAsync_Directory_AppendsSubfolderStructure()
    {
        // Arrange - Create base archive
        var initialDir = Path.Combine(_testDir, "dir_append_initial");
        Directory.CreateDirectory(initialDir);
        await File.WriteAllTextAsync(Path.Combine(initialDir, "root.txt"), "Root content");

        var archivePath = Path.Combine(_testDir, "dir_append.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(initialDir, archivePath, 9));

        // Create extra directory structure to append
        var extraDir = Path.Combine(_testDir, "extra_folder");
        Directory.CreateDirectory(Path.Combine(extraDir, "nested"));
        await File.WriteAllTextAsync(Path.Combine(extraDir, "doc.txt"), "Doc content");
        await File.WriteAllTextAsync(Path.Combine(extraDir, "nested", "inner.txt"), "Inner content");

        // Act
        var appendReq = new ArchiveAppendRequest(archivePath, [extraDir], 9);
        var result = await _engine.AppendAsync(appendReq);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.AddedFiles);
        Assert.Equal(1, result.RetainedFiles);
        Assert.Equal(3, result.TotalFiles);

        var extractDir = Path.Combine(_testDir, "dir_append_extracted");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));

        Assert.Equal("Root content", await File.ReadAllTextAsync(Path.Combine(extractDir, "root.txt")));
        Assert.Equal("Doc content", await File.ReadAllTextAsync(Path.Combine(extractDir, "extra_folder", "doc.txt")));
        Assert.Equal("Inner content", await File.ReadAllTextAsync(Path.Combine(extractDir, "extra_folder", "nested", "inner.txt")));
    }

    [Fact]
    public async Task AppendAsync_CollidingEntry_Default_OverwritesExistingContent()
    {
        // Arrange - Create archive with initial content
        var baseDir = Path.Combine(_testDir, "overwrite_base");
        Directory.CreateDirectory(baseDir);
        var baseFile = Path.Combine(baseDir, "data.txt");
        await File.WriteAllTextAsync(baseFile, "Original Version");

        var archivePath = Path.Combine(_testDir, "overwrite_test.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, archivePath, 9));

        // Create updated file with same relative name
        var updatedDir = Path.Combine(_testDir, "overwrite_new");
        Directory.CreateDirectory(updatedDir);
        var updatedFile = Path.Combine(updatedDir, "data.txt");
        await File.WriteAllTextAsync(updatedFile, "Updated Version 2.0");

        // Act - Append without update-only
        var appendReq = new ArchiveAppendRequest(archivePath, ["data.txt"], 9, BaseDirectory: updatedDir);
        var result = await _engine.AppendAsync(appendReq);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.AddedFiles);
        Assert.Equal(1, result.UpdatedFiles);
        Assert.Equal(0, result.RetainedFiles);
        Assert.Equal(0, result.SkippedFiles);
        Assert.Equal(1, result.TotalFiles);

        var extractDir = Path.Combine(_testDir, "overwrite_extracted");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));

        Assert.Equal("Updated Version 2.0", await File.ReadAllTextAsync(Path.Combine(extractDir, "data.txt")));
    }

    [Fact]
    public async Task AppendAsync_CollidingEntry_UpdateOnly_WhenSourceNewer_OverwritesExisting()
    {
        // Arrange - Base archive
        var baseDir = Path.Combine(_testDir, "update_only_newer_base");
        Directory.CreateDirectory(baseDir);
        var baseFile = Path.Combine(baseDir, "log.txt");
        await File.WriteAllTextAsync(baseFile, "Old log");
        var pastTime = DateTime.UtcNow.AddHours(-2);
        File.SetLastWriteTimeUtc(baseFile, pastTime);

        var archivePath = Path.Combine(_testDir, "update_only_newer.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, archivePath, 9));

        // Newer incoming source
        var newDir = Path.Combine(_testDir, "update_only_newer_in");
        Directory.CreateDirectory(newDir);
        var newFile = Path.Combine(newDir, "log.txt");
        await File.WriteAllTextAsync(newFile, "New log");
        var nowTime = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(newFile, nowTime);

        // Act
        var appendReq = new ArchiveAppendRequest(archivePath, ["log.txt"], 9, UpdateOnly: true, BaseDirectory: newDir);
        var result = await _engine.AppendAsync(appendReq);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.UpdatedFiles);
        Assert.Equal(0, result.RetainedFiles);
        Assert.Equal(0, result.SkippedFiles);
        Assert.Equal(1, result.TotalFiles);

        var extractDir = Path.Combine(_testDir, "update_only_newer_extracted");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));
        Assert.Equal("New log", await File.ReadAllTextAsync(Path.Combine(extractDir, "log.txt")));
    }

    [Fact]
    public async Task AppendAsync_CollidingEntry_UpdateOnly_WhenSourceOlderOrSame_RetainsExistingAndSkipsSource()
    {
        // Arrange - Base archive with newer timestamp
        var baseDir = Path.Combine(_testDir, "update_only_older_base");
        Directory.CreateDirectory(baseDir);
        var baseFile = Path.Combine(baseDir, "config.json");
        await File.WriteAllTextAsync(baseFile, "{\"version\": 2}");
        var newerTime = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(baseFile, newerTime);

        var archivePath = Path.Combine(_testDir, "update_only_older.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, archivePath, 9));

        // Older incoming source
        var oldDir = Path.Combine(_testDir, "update_only_older_in");
        Directory.CreateDirectory(oldDir);
        var oldFile = Path.Combine(oldDir, "config.json");
        await File.WriteAllTextAsync(oldFile, "{\"version\": 1}");
        var olderTime = DateTime.UtcNow.AddHours(-3);
        File.SetLastWriteTimeUtc(oldFile, olderTime);

        // Act
        var appendReq = new ArchiveAppendRequest(archivePath, ["config.json"], 9, UpdateOnly: true, BaseDirectory: oldDir);
        var result = await _engine.AppendAsync(appendReq);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.AddedFiles);
        Assert.Equal(0, result.UpdatedFiles);
        Assert.Equal(1, result.RetainedFiles);
        Assert.Equal(1, result.SkippedFiles);
        Assert.Equal(1, result.TotalFiles);

        var extractDir = Path.Combine(_testDir, "update_only_older_extracted");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));
        Assert.Equal("{\"version\": 2}", await File.ReadAllTextAsync(Path.Combine(extractDir, "config.json")));
    }

    [Fact]
    public async Task AppendAsync_TracksProgressSmoothly_AcrossRetainedAndIncomingEntries()
    {
        // Arrange
        var baseDir = Path.Combine(_testDir, "prog_base");
        Directory.CreateDirectory(baseDir);
        var f1 = Path.Combine(baseDir, "existing.bin");
        var f1Data = new byte[32 * 1024];
        Random.Shared.NextBytes(f1Data);
        await File.WriteAllBytesAsync(f1, f1Data);

        var archivePath = Path.Combine(_testDir, "prog_test.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, archivePath, 9));

        var f2 = Path.Combine(_testDir, "incoming.bin");
        var f2Data = new byte[64 * 1024];
        Random.Shared.NextBytes(f2Data);
        await File.WriteAllBytesAsync(f2, f2Data);

        var progressReports = new List<ProgressReport>();
        var progress = new SynchronousProgress<ProgressReport>(r => progressReports.Add(r));

        // Act
        var appendReq = new ArchiveAppendRequest(archivePath, [f2], 9);
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
    public async Task AppendAsync_AtomicRollback_WhenErrorOccurs_PreservesOriginalArchiveAndCleansTempFile()
    {
        // Arrange
        var baseDir = Path.Combine(_testDir, "atomic_base");
        Directory.CreateDirectory(baseDir);
        var baseFile = Path.Combine(baseDir, "original.txt");
        await File.WriteAllTextAsync(baseFile, "Original untouched content");

        var archivePath = Path.Combine(_testDir, "atomic_test.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, archivePath, 9));
        var originalBytes = await File.ReadAllBytesAsync(archivePath);

        // Non-existent source triggers failure upfront
        var missingSource = Path.Combine(_testDir, "does_not_exist_source.txt");

        // Act & Assert
        var appendReq = new ArchiveAppendRequest(archivePath, [missingSource], 9);
        await Assert.ThrowsAsync<FileNotFoundException>(() => _engine.AppendAsync(appendReq));

        // Original archive must be identical and no temp files left
        var currentBytes = await File.ReadAllBytesAsync(archivePath);
        Assert.Equal(originalBytes, currentBytes);

        var tempFiles = Directory.GetFiles(_testDir, "atomic_test.zrus.tmp.*");
        Assert.Empty(tempFiles);
    }

    [Fact]
    public async Task AppendAsync_NonExistentArchive_ThrowsFileNotFoundException()
    {
        var missingArchive = Path.Combine(_testDir, "missing.zrus");
        var sourceFile = Path.Combine(_testDir, "some_file.txt");
        await File.WriteAllTextAsync(sourceFile, "Some content");

        var req = new ArchiveAppendRequest(missingArchive, [sourceFile], 9);
        await Assert.ThrowsAsync<FileNotFoundException>(() => _engine.AppendAsync(req));
    }

    [Fact]
    public async Task AppendAsync_InvalidCompressionLevel_ThrowsArgumentOutOfRangeException()
    {
        var baseDir = Path.Combine(_testDir, "level_base");
        Directory.CreateDirectory(baseDir);
        var file = Path.Combine(baseDir, "file.txt");
        await File.WriteAllTextAsync(file, "content");

        var archivePath = Path.Combine(_testDir, "level_test.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, archivePath, 9));

        var reqUnder = new ArchiveAppendRequest(archivePath, [file], 0);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _engine.AppendAsync(reqUnder));

        var reqOver = new ArchiveAppendRequest(archivePath, [file], 23);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _engine.AppendAsync(reqOver));
    }

    [Fact]
    public async Task AppendAsync_EmptySources_ThrowsArgumentException()
    {
        var baseDir = Path.Combine(_testDir, "empty_src_base");
        Directory.CreateDirectory(baseDir);
        var file = Path.Combine(baseDir, "file.txt");
        await File.WriteAllTextAsync(file, "content");

        var archivePath = Path.Combine(_testDir, "empty_src_test.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(baseDir, archivePath, 9));

        var req = new ArchiveAppendRequest(archivePath, [], 9);
        await Assert.ThrowsAsync<ArgumentException>(() => _engine.AppendAsync(req));
    }

    #endregion
}

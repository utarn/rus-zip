using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

public class SafeArchiveExtractorTests : IDisposable
{
    private readonly string _testDir;

    public SafeArchiveExtractorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ruszip_safe_extract_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { /* Ignore */ }
        }
    }

    private class FakeExtractionSource(IEnumerable<ExtractionEntry> entries) : IArchiveExtractionSource
    {
        public async IAsyncEnumerable<ExtractionEntry> ReadEntriesAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                yield return entry;
            }
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ExtractAllAsync_ExtractsFilesAndDirectories_WithTimestamps()
    {
        var targetDir = Path.Combine(_testDir, "out");
        var expectedTime = new DateTimeOffset(2025, 5, 1, 12, 0, 0, TimeSpan.Zero);

        var entries = new List<ExtractionEntry>
        {
            new("folder", true, 0, expectedTime, null, _ => ValueTask.FromResult<Stream>(Stream.Null)),
            new("folder/nested", true, 0, expectedTime, null, _ => ValueTask.FromResult<Stream>(Stream.Null)),
            new("folder/nested/hello.txt", false, 12, expectedTime, null, _ => ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("hello world!"))))
        };

        var source = new FakeExtractionSource(entries);
        var reports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(r => reports.Add(r));

        await SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 12, progress);

        var extractedFile = Path.Combine(targetDir, "folder", "nested", "hello.txt");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal("hello world!", await File.ReadAllTextAsync(extractedFile));
        Assert.Equal(expectedTime.UtcDateTime, File.GetLastWriteTimeUtc(extractedFile));

        var extractedDir = Path.Combine(targetDir, "folder", "nested");
        Assert.True(Directory.Exists(extractedDir));
        Assert.Equal(expectedTime.UtcDateTime, Directory.GetLastWriteTimeUtc(extractedDir));

        var extractedParentDir = Path.Combine(targetDir, "folder");
        Assert.True(Directory.Exists(extractedParentDir));
        Assert.Equal(expectedTime.UtcDateTime, Directory.GetLastWriteTimeUtc(extractedParentDir));
    }

    [Theory]
    [InlineData("../../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("folder/../../../secret.txt")]
    public async Task ExtractAllAsync_MaliciousPathTraversal_ThrowsSecurityException(string maliciousPath)
    {
        var targetDir = Path.Combine(_testDir, "out");
        var entries = new List<ExtractionEntry>
        {
            new(maliciousPath, false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("evil"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);

        var ex = await Assert.ThrowsAsync<SecurityException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 4, null));

        Assert.Contains("path traversal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractAllAsync_OverwriteFalse_ThrowsWhenFileExists()
    {
        var targetDir = Path.Combine(_testDir, "out");
        Directory.CreateDirectory(targetDir);
        var existingFile = Path.Combine(targetDir, "file.txt");
        await File.WriteAllTextAsync(existingFile, "original");

        var entries = new List<ExtractionEntry>
        {
            new("file.txt", false, 3, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);

        await Assert.ThrowsAsync<IOException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: false, totalBytes: 3, null));
    }

    [Fact]
    public async Task ExtractAllAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var targetDir = Path.Combine(_testDir, "out");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var entries = new List<ExtractionEntry>
        {
            new("file.txt", false, 3, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 3, null, cts.Token));
    }

    [Fact]
    public async Task ExtractAllAsync_DirectoryMetadataRestoredBottomUp_PreservesDeepDirectoryTimestamps()
    {
        var targetDir = Path.Combine(_testDir, "bottom_up_out");
        var parentTime = new DateTimeOffset(2023, 1, 10, 10, 0, 0, TimeSpan.Zero);
        var childTime = new DateTimeOffset(2024, 2, 20, 14, 30, 0, TimeSpan.Zero);
        var fileTime = new DateTimeOffset(2025, 3, 30, 16, 45, 0, TimeSpan.Zero);

        var entries = new List<ExtractionEntry>
        {
            new("level1", true, 0, parentTime, null, _ => ValueTask.FromResult<Stream>(Stream.Null)),
            new("level1/level2", true, 0, childTime, null, _ => ValueTask.FromResult<Stream>(Stream.Null)),
            new("level1/level2/data.bin", false, 5, fileTime, null, _ => ValueTask.FromResult<Stream>(new MemoryStream([1, 2, 3, 4, 5])))
        };

        var source = new FakeExtractionSource(entries);
        await SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 5, null);

        var extractedParent = Path.Combine(targetDir, "level1");
        var extractedChild = Path.Combine(targetDir, "level1", "level2");
        var extractedFile = Path.Combine(targetDir, "level1", "level2", "data.bin");

        Assert.Equal(parentTime.UtcDateTime, Directory.GetLastWriteTimeUtc(extractedParent));
        Assert.Equal(childTime.UtcDateTime, Directory.GetLastWriteTimeUtc(extractedChild));
        Assert.Equal(fileTime.UtcDateTime, File.GetLastWriteTimeUtc(extractedFile));
    }

    [Fact]
    public async Task ExtractAllAsync_PreservesPosixModes_WhenNonWindows()
    {
        if (OperatingSystem.IsWindows())
            return;

        var targetDir = Path.Combine(_testDir, "posix_out");
        var execMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                       UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
        var dirMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        var entries = new List<ExtractionEntry>
        {
            new("scripts", true, 0, null, dirMode, _ => ValueTask.FromResult<Stream>(Stream.Null)),
            new("scripts/run.sh", false, 11, null, execMode, _ => ValueTask.FromResult<Stream>(new MemoryStream("#!/bin/sh\n"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);
        await SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 11, null);

        var extractedScript = Path.Combine(targetDir, "scripts", "run.sh");
        var extractedDir = Path.Combine(targetDir, "scripts");

        Assert.Equal(execMode, File.GetUnixFileMode(extractedScript));
        Assert.Equal(dirMode, File.GetUnixFileMode(extractedDir));
    }

    [Fact]
    public async Task ExtractAllAsync_ReportsProgress_DeterminateAndIndeterminate()
    {
        var targetDir = Path.Combine(_testDir, "prog_out");
        var payload = new byte[100 * 1024]; // 100 KB
        Random.Shared.NextBytes(payload);

        var entries = new List<ExtractionEntry>
        {
            new("chunked.dat", false, payload.Length, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream(payload)))
        };

        var source = new FakeExtractionSource(entries);
        var reports = new List<ProgressReport>();
        var progress = new SyncProgress<ProgressReport>(reports.Add);

        await SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: payload.Length, progress);

        Assert.NotEmpty(reports);
        Assert.Equal(payload.Length, reports.Last().ProcessedBytes);
        Assert.False(reports.Last().IsIndeterminate);
        Assert.Equal(100.0, reports.Last().Percentage);
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Theory]
    [InlineData(".. ")]
    [InlineData(".. .")]
    [InlineData("file.")]
    [InlineData("file ")]
    [InlineData("folder./file")]
    [InlineData("folder/file.")]
    public async Task ExtractAllAsync_PathComponentEndingInDotOrSpace_ThrowsSecurityException(string maliciousPath)
    {
        var targetDir = Path.Combine(_testDir, "trailing_out");
        var entries = new List<ExtractionEntry>
        {
            new(maliciousPath, false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("evil"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);

        var ex = await Assert.ThrowsAsync<SecurityException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 4, null));

        Assert.Contains("Malicious entry detected", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path traversal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractAllAsync_SymlinkedParent_ThrowsSecurityException_AndDoesNotWriteOutside()
    {
        var targetDir = Path.Combine(_testDir, "symlink_out");
        Directory.CreateDirectory(targetDir);
        var outsideDir = Path.Combine(_testDir, "symlink_outside");
        Directory.CreateDirectory(outsideDir);

        var linkPath = Path.Combine(targetDir, "evil");
        try
        {
            File.CreateSymbolicLink(linkPath, outsideDir);
        }
        catch (Exception) when (OperatingSystem.IsWindows())
        {
            return; // Windows may require elevated privileges / Developer Mode to create symlinks.
        }

        var entries = new List<ExtractionEntry>
        {
            new("evil/escaped.txt", false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("evil"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);

        var ex = await Assert.ThrowsAsync<SecurityException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 4, null));

        Assert.Contains("symlinked", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(outsideDir, "escaped.txt")));
    }

    [Fact]
    public async Task ExtractAllAsync_SecurityExceptionAbort_RemovesPartiallyCreatedFiles()
    {
        var targetDir = Path.Combine(_testDir, "cleanup_out");
        var entries = new List<ExtractionEntry>
        {
            new("good.txt", false, 1, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("g"u8.ToArray()))),
            new("../evil.txt", false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("evil"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);

        var ex = await Assert.ThrowsAsync<SecurityException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 5, null));

        Assert.Contains("path traversal", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(targetDir, "good.txt")));
        Assert.Empty(Directory.GetFiles(targetDir));
    }

    [Fact]
    public async Task ExtractAllAsync_SecurityExceptionAbort_RemovesPartiallyCreatedDirectories()
    {
        var targetDir = Path.Combine(_testDir, "cleanup_dirs_out");
        var entries = new List<ExtractionEntry>
        {
            new("folder", true, 0, null, null, _ => ValueTask.FromResult<Stream>(Stream.Null)),
            new("folder/nested", true, 0, null, null, _ => ValueTask.FromResult<Stream>(Stream.Null)),
            new("folder/nested/data.txt", false, 1, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("d"u8.ToArray()))),
            new("file.", false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("evil"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);

        await Assert.ThrowsAsync<SecurityException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 5, null));

        Assert.False(Directory.Exists(Path.Combine(targetDir, "folder")));
        Assert.False(File.Exists(Path.Combine(targetDir, "folder", "nested", "data.txt")));
    }

    [Fact]
    public async Task ExtractAllAsync_SizeCapExceeded_ThrowsLimitException_AndCleansUpPartialFile()
    {
        var targetDir = Path.Combine(_testDir, "bomb_size_out");
        // Metadata claims a tiny size but the stream actually expands well past the cap.
        var payload = new byte[2 * 1024 * 1024]; // 2 MB
        Random.Shared.NextBytes(payload);

        var entries = new List<ExtractionEntry>
        {
            new("bomb.bin", false, UncompressedSize: 10, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream(payload)))
        };

        var source = new FakeExtractionSource(entries);
        var limits = new ExtractionLimits(MaxCumulativeUncompressedBytes: 1 * 1024 * 1024, MaxEntryCount: null);

        var ex = await Assert.ThrowsAsync<ExtractionLimitExceededException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 10, null, limits: limits));

        Assert.Contains("uncompressed output", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--max-uncompressed-size", ex.Message, StringComparison.OrdinalIgnoreCase);
        // Partial output must be cleaned up.
        Assert.False(File.Exists(Path.Combine(targetDir, "bomb.bin")));
        Assert.Empty(Directory.GetFiles(targetDir));
    }

    [Fact]
    public async Task ExtractAllAsync_EntryCountCapExceeded_ThrowsLimitException_AndCleansUp()
    {
        var targetDir = Path.Combine(_testDir, "bomb_entries_out");
        var entries = Enumerable.Range(0, 5)
            .Select(i => new ExtractionEntry($"file{i}.txt", false, 1, null, null,
                _ => ValueTask.FromResult<Stream>(new MemoryStream("x"u8.ToArray()))))
            .ToList();

        var source = new FakeExtractionSource(entries);
        var limits = new ExtractionLimits(MaxCumulativeUncompressedBytes: null, MaxEntryCount: 3);

        var ex = await Assert.ThrowsAsync<ExtractionLimitExceededException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 5, null, limits: limits));

        Assert.Contains("entry count", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--max-entries", ex.Message, StringComparison.OrdinalIgnoreCase);
        // The first three files were created, then aborted; all partial output must be removed.
        Assert.Empty(Directory.GetFiles(targetDir));
    }

    [Fact]
    public async Task ExtractAllAsync_UnlimitedLimits_AllowsBombToComplete()
    {
        var targetDir = Path.Combine(_testDir, "unlimited_out");
        var payload = new byte[2 * 1024 * 1024]; // 2 MB
        Random.Shared.NextBytes(payload);

        var entries = new List<ExtractionEntry>
        {
            new("big.bin", false, UncompressedSize: 10, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream(payload)))
        };

        var source = new FakeExtractionSource(entries);
        // null/0 limits mean unlimited on both dimensions.
        var limits = new ExtractionLimits(MaxCumulativeUncompressedBytes: null, MaxEntryCount: 0);

        var result = await SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 10, null, limits: limits);

        Assert.Equal(payload.Length, result.BytesExtracted);
        Assert.Equal(1, result.FilesExtracted);
        Assert.Equal(1, result.EntriesProcessed);
        Assert.True(File.Exists(Path.Combine(targetDir, "big.bin")));
    }

    [Fact]
    public async Task ExtractAllAsync_SpoofedMetadata_ExtractsRealBytes_AndReportsRealTotals()
    {
        var targetDir = Path.Combine(_testDir, "spoofed_out");
        var realPayload = "12345678"u8.ToArray(); // 8 real bytes
        // Header declares ~2 GB but the stored content is only 8 bytes.
        const long spoofedSize = 2L * 1024 * 1024 * 1024;

        var entries = new List<ExtractionEntry>
        {
            new("small.txt", false, UncompressedSize: spoofedSize, null, null,
                _ => ValueTask.FromResult<Stream>(new MemoryStream(realPayload)))
        };

        var source = new FakeExtractionSource(entries);

        var result = await SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: spoofedSize, null);

        Assert.Equal(realPayload.Length, result.BytesExtracted);
        Assert.Equal(1, result.FilesExtracted);
        Assert.Equal(realPayload, await File.ReadAllBytesAsync(Path.Combine(targetDir, "small.txt")));
    }

    [Fact]
    public async Task ExtractAllAsync_DefaultsApply_WhenLimitsAreNull_AndReturnsActualTotals()
    {
        var targetDir = Path.Combine(_testDir, "defaults_out");
        var entries = new List<ExtractionEntry>
        {
            new("a.txt", false, 2, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("ab"u8.ToArray()))),
            new("sub", true, 0, null, null, _ => ValueTask.FromResult<Stream>(Stream.Null)),
            new("sub/b.txt", false, 3, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("cde"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);
        var result = await SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 5, null);

        Assert.Equal(5, result.BytesExtracted);
        Assert.Equal(2, result.FilesExtracted);
        Assert.Equal(3, result.EntriesProcessed);
    }

    [Fact]
    public async Task ExtractAllAsync_FileEntryCollidingWithExistingDirectory_ThrowsIOException()
    {
        var targetDir = Path.Combine(_testDir, "collision_file_vs_dir");
        Directory.CreateDirectory(Path.Combine(targetDir, "folder")); // pre-existing directory

        var entries = new List<ExtractionEntry>
        {
            new("folder", false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("data"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);

        var ex = await Assert.ThrowsAsync<IOException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 4, null));

        Assert.Contains("folder", ex.Message);
        Assert.Contains("already exists as a directory", ex.Message, StringComparison.OrdinalIgnoreCase);
        // The colliding file must not have been written through the directory.
        Assert.False(File.Exists(Path.Combine(targetDir, "folder")) || Directory.GetFiles(Path.Combine(targetDir, "folder")).Length > 0);
    }

    [Fact]
    public async Task ExtractAllAsync_DirectoryEntryCollidingWithExistingFile_ThrowsIOException()
    {
        var targetDir = Path.Combine(_testDir, "collision_dir_vs_file");
        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(Path.Combine(targetDir, "folder"), "existing");

        var entries = new List<ExtractionEntry>
        {
            new("folder", true, 0, null, null, _ => ValueTask.FromResult<Stream>(Stream.Null))
        };

        var source = new FakeExtractionSource(entries);

        var ex = await Assert.ThrowsAsync<IOException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 0, null));

        Assert.Contains("folder", ex.Message);
        Assert.Contains("already exists as a file", ex.Message, StringComparison.OrdinalIgnoreCase);
        // The pre-existing file must remain untouched.
        Assert.True(File.Exists(Path.Combine(targetDir, "folder")));
        Assert.Equal("existing", await File.ReadAllTextAsync(Path.Combine(targetDir, "folder")));
    }
}

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
        var progress = new Progress<ProgressReport>(reports.Add);

        await SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: payload.Length, progress);

        Assert.NotEmpty(reports);
        Assert.Equal(payload.Length, reports.Last().ProcessedBytes);
        Assert.False(reports.Last().IsIndeterminate);
        Assert.Equal(100.0, reports.Last().Percentage);
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
}

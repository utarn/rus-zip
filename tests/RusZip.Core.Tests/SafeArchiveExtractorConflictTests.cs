using System.Runtime.CompilerServices;
using System.Text;
using RusZip.Core.Abstractions;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

public class SafeArchiveExtractorConflictTests : IDisposable
{
    private readonly string _testDir;

    public SafeArchiveExtractorConflictTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ruszip_conflict_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { /* Ignore */ }
        }
    }

    private sealed class FakeExtractionSource(IEnumerable<ExtractionEntry> entries) : IArchiveExtractionSource
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

    private sealed class CallbackConflictResolver(Func<FileConflictContext, CancellationToken, ValueTask<FileConflictResolution>> callback) : IFileConflictResolver
    {
        public List<FileConflictContext> Invocations { get; } = new();

        public ValueTask<FileConflictResolution> ResolveConflictAsync(FileConflictContext context, CancellationToken cancellationToken = default)
        {
            Invocations.Add(context);
            return callback(context, cancellationToken);
        }
    }

    private sealed class DelegateProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Fact]
    public async Task ResolveConflict_WhenFileDoesNotExist_ResolverNotCalledAndFileExtracted()
    {
        var targetDir = Path.Combine(_testDir, "no_conflict_out");
        var entries = new List<ExtractionEntry>
        {
            new("file1.txt", false, 5, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("hello"u8.ToArray())))
        };

        var resolver = new CallbackConflictResolver((_, _) => ValueTask.FromResult(FileConflictResolution.Overwrite));
        var source = new FakeExtractionSource(entries);

        var result = await SafeArchiveExtractor.ExtractAllAsync(
            source,
            targetDir,
            overwrite: false,
            totalBytes: 5,
            progress: null,
            conflictResolver: resolver);

        Assert.Empty(resolver.Invocations);
        Assert.Equal(1, result.FilesExtracted);
        Assert.Equal(5, result.BytesExtracted);
        Assert.Equal(1, result.EntriesProcessed);
        Assert.True(File.Exists(Path.Combine(targetDir, "file1.txt")));
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(targetDir, "file1.txt")));
    }

    [Fact]
    public async Task ResolveConflict_Overwrite_OverwritesDestinationAndUpdatesMetrics()
    {
        var targetDir = Path.Combine(_testDir, "overwrite_out");
        Directory.CreateDirectory(targetDir);
        var existingFile = Path.Combine(targetDir, "file.txt");
        await File.WriteAllTextAsync(existingFile, "original content");

        var modTime = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var entries = new List<ExtractionEntry>
        {
            new("file.txt", false, 11, modTime, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new content"u8.ToArray())))
        };

        var resolver = new CallbackConflictResolver((ctx, _) =>
        {
            Assert.Equal(existingFile, ctx.TargetPath);
            Assert.Equal("file.txt", ctx.RelativeEntryPath);
            Assert.Equal(11, ctx.EntryUncompressedSize);
            Assert.Equal(modTime, ctx.EntryLastModified);
            Assert.Equal("original content".Length, ctx.ExistingFileSize);
            return ValueTask.FromResult(FileConflictResolution.Overwrite);
        });

        var source = new FakeExtractionSource(entries);

        var result = await SafeArchiveExtractor.ExtractAllAsync(
            source,
            targetDir,
            overwrite: false,
            totalBytes: 11,
            progress: null,
            conflictResolver: resolver);

        Assert.Single(resolver.Invocations);
        Assert.Equal(1, result.FilesExtracted);
        Assert.Equal(11, result.BytesExtracted);
        Assert.Equal(1, result.EntriesProcessed);
        Assert.Equal("new content", await File.ReadAllTextAsync(existingFile));
    }

    [Fact]
    public async Task ResolveConflict_OverwriteAll_SetsBatchPolicy_OverwritesAllWithoutSubsequentPrompts()
    {
        var targetDir = Path.Combine(_testDir, "overwrite_all_out");
        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(Path.Combine(targetDir, "file1.txt"), "orig1");
        await File.WriteAllTextAsync(Path.Combine(targetDir, "file2.txt"), "orig2");

        var entries = new List<ExtractionEntry>
        {
            new("file1.txt", false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new1"u8.ToArray()))),
            new("file2.txt", false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new2"u8.ToArray()))),
            new("file3.txt", false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new3"u8.ToArray())))
        };

        var resolver = new CallbackConflictResolver((_, _) => ValueTask.FromResult(FileConflictResolution.OverwriteAll));
        var source = new FakeExtractionSource(entries);

        var result = await SafeArchiveExtractor.ExtractAllAsync(
            source,
            targetDir,
            overwrite: false,
            totalBytes: 12,
            progress: null,
            conflictResolver: resolver);

        Assert.Single(resolver.Invocations);
        Assert.Equal(3, result.FilesExtracted);
        Assert.Equal(12, result.BytesExtracted);
        Assert.Equal(3, result.EntriesProcessed);
        Assert.Equal("new1", await File.ReadAllTextAsync(Path.Combine(targetDir, "file1.txt")));
        Assert.Equal("new2", await File.ReadAllTextAsync(Path.Combine(targetDir, "file2.txt")));
        Assert.Equal("new3", await File.ReadAllTextAsync(Path.Combine(targetDir, "file3.txt")));
    }

    [Fact]
    public async Task ResolveConflict_Skip_SkipsExtraction_AdvancesProgress_DoesNotOverwrite()
    {
        var targetDir = Path.Combine(_testDir, "skip_out");
        Directory.CreateDirectory(targetDir);
        var existingFile = Path.Combine(targetDir, "existing.txt");
        await File.WriteAllTextAsync(existingFile, "original");

        var entries = new List<ExtractionEntry>
        {
            new("existing.txt", false, 3, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new"u8.ToArray()))),
            new("fresh.txt", false, 5, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("fresh"u8.ToArray())))
        };

        var reportedFiles = new List<string>();
        var progress = new DelegateProgress<ProgressReport>(r =>
        {
            if (r.CurrentFileName != null)
            {
                reportedFiles.Add(r.CurrentFileName);
            }
        });

        var resolver = new CallbackConflictResolver((_, _) => ValueTask.FromResult(FileConflictResolution.Skip));
        var source = new FakeExtractionSource(entries);

        var result = await SafeArchiveExtractor.ExtractAllAsync(
            source,
            targetDir,
            overwrite: false,
            totalBytes: 8,
            progress: progress,
            conflictResolver: resolver);

        Assert.Single(resolver.Invocations);
        Assert.Equal("original", await File.ReadAllTextAsync(existingFile));
        Assert.Equal("fresh", await File.ReadAllTextAsync(Path.Combine(targetDir, "fresh.txt")));
        Assert.Equal(1, result.FilesExtracted);
        Assert.Equal(5, result.BytesExtracted);
        Assert.Equal(2, result.EntriesProcessed);
        Assert.Contains("existing.txt", reportedFiles);
        Assert.Contains("fresh.txt", reportedFiles);
    }

    [Fact]
    public async Task ResolveConflict_SkipAll_SetsBatchPolicy_SkipsAllWithoutSubsequentPrompts()
    {
        var targetDir = Path.Combine(_testDir, "skip_all_out");
        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(Path.Combine(targetDir, "file1.txt"), "orig1");
        await File.WriteAllTextAsync(Path.Combine(targetDir, "file2.txt"), "orig2");

        var entries = new List<ExtractionEntry>
        {
            new("file1.txt", false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new1"u8.ToArray()))),
            new("file2.txt", false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new2"u8.ToArray()))),
            new("file3.txt", false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new3"u8.ToArray())))
        };

        var resolver = new CallbackConflictResolver((_, _) => ValueTask.FromResult(FileConflictResolution.SkipAll));
        var source = new FakeExtractionSource(entries);

        var result = await SafeArchiveExtractor.ExtractAllAsync(
            source,
            targetDir,
            overwrite: false,
            totalBytes: 12,
            progress: null,
            conflictResolver: resolver);

        Assert.Single(resolver.Invocations);
        Assert.Equal(1, result.FilesExtracted);
        Assert.Equal(4, result.BytesExtracted);
        Assert.Equal(3, result.EntriesProcessed);
        Assert.Equal("orig1", await File.ReadAllTextAsync(Path.Combine(targetDir, "file1.txt")));
        Assert.Equal("orig2", await File.ReadAllTextAsync(Path.Combine(targetDir, "file2.txt")));
        Assert.Equal("new3", await File.ReadAllTextAsync(Path.Combine(targetDir, "file3.txt")));
    }

    [Fact]
    public async Task ResolveConflict_Abort_ThrowsOperationCanceledException_AndCleansUpCreatedFiles()
    {
        var targetDir = Path.Combine(_testDir, "abort_out");
        Directory.CreateDirectory(targetDir);
        var existingFile = Path.Combine(targetDir, "conflict.txt");
        await File.WriteAllTextAsync(existingFile, "original");

        var entries = new List<ExtractionEntry>
        {
            new("created_before.txt", false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("data"u8.ToArray()))),
            new("conflict.txt", false, 3, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new"u8.ToArray())))
        };

        var resolver = new CallbackConflictResolver((_, _) => ValueTask.FromResult(FileConflictResolution.Abort));
        var source = new FakeExtractionSource(entries);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(
                source,
                targetDir,
                overwrite: false,
                totalBytes: 7,
                progress: null,
                conflictResolver: resolver));

        // Newly created file must be cleaned up
        Assert.False(File.Exists(Path.Combine(targetDir, "created_before.txt")));
        // Pre-existing file must NOT be deleted or modified
        Assert.True(File.Exists(existingFile));
        Assert.Equal("original", await File.ReadAllTextAsync(existingFile));
    }

    [Fact]
    public async Task ResolveConflict_PerFilePrompting_MultipleConflicts_HandledIndividually()
    {
        var targetDir = Path.Combine(_testDir, "per_file_out");
        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(Path.Combine(targetDir, "file1.txt"), "orig1");
        await File.WriteAllTextAsync(Path.Combine(targetDir, "file2.txt"), "orig2");

        var entries = new List<ExtractionEntry>
        {
            new("file1.txt", false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new1"u8.ToArray()))),
            new("file2.txt", false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new2"u8.ToArray())))
        };

        int callCount = 0;
        var resolver = new CallbackConflictResolver((ctx, _) =>
        {
            callCount++;
            return callCount switch
            {
                1 => ValueTask.FromResult(FileConflictResolution.Overwrite),
                2 => ValueTask.FromResult(FileConflictResolution.Skip),
                _ => throw new InvalidOperationException()
            };
        });

        var source = new FakeExtractionSource(entries);

        var result = await SafeArchiveExtractor.ExtractAllAsync(
            source,
            targetDir,
            overwrite: false,
            totalBytes: 8,
            progress: null,
            conflictResolver: resolver);

        Assert.Equal(2, resolver.Invocations.Count);
        Assert.Equal(1, result.FilesExtracted);
        Assert.Equal(4, result.BytesExtracted);
        Assert.Equal(2, result.EntriesProcessed);
        Assert.Equal("new1", await File.ReadAllTextAsync(Path.Combine(targetDir, "file1.txt")));
        Assert.Equal("orig2", await File.ReadAllTextAsync(Path.Combine(targetDir, "file2.txt")));
    }

    [Fact]
    public async Task ExtractAsync_NullConflictResolver_FallsBackToOverwriteFlag_WhenOverwriteFalse_ThrowsIOException()
    {
        var targetDir = Path.Combine(_testDir, "null_resolver_false_out");
        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(Path.Combine(targetDir, "file.txt"), "original");

        var entries = new List<ExtractionEntry>
        {
            new("file.txt", false, 3, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);

        await Assert.ThrowsAsync<IOException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(
                source,
                targetDir,
                overwrite: false,
                totalBytes: 3,
                progress: null,
                conflictResolver: null));
    }

    [Fact]
    public async Task ExtractAsync_NullConflictResolver_FallsBackToOverwriteFlag_WhenOverwriteTrue_Overwrites()
    {
        var targetDir = Path.Combine(_testDir, "null_resolver_true_out");
        Directory.CreateDirectory(targetDir);
        var existingFile = Path.Combine(targetDir, "file.txt");
        await File.WriteAllTextAsync(existingFile, "original");

        var entries = new List<ExtractionEntry>
        {
            new("file.txt", false, 3, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);

        var result = await SafeArchiveExtractor.ExtractAllAsync(
            source,
            targetDir,
            overwrite: true,
            totalBytes: 3,
            progress: null,
            conflictResolver: null);

        Assert.Equal(1, result.FilesExtracted);
        Assert.Equal("new", await File.ReadAllTextAsync(existingFile));
    }

    [Fact]
    public async Task ExtractAsync_UnifiedEngine_ZstdTar_WithConflictResolver_HandlesSkipAndOverwrite()
    {
        var engine = new UnifiedArchiveEngine();
        var sourceDir = Path.Combine(_testDir, "zstd_src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "doc.txt"), "archive version");

        var zrusPath = Path.Combine(_testDir, "test.zrus");
        await engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zrusPath, 3));

        // Test Skip
        var destDirSkip = Path.Combine(_testDir, "zstd_dest_skip");
        Directory.CreateDirectory(destDirSkip);
        await File.WriteAllTextAsync(Path.Combine(destDirSkip, "doc.txt"), "pre-existing version");

        var skipResolver = new CallbackConflictResolver((_, _) => ValueTask.FromResult(FileConflictResolution.Skip));
        var skipReq = new ArchiveExtractionRequest(zrusPath, destDirSkip, Overwrite: false, ConflictResolver: skipResolver);
        var skipResult = await engine.ExtractAsync(skipReq);

        Assert.Single(skipResolver.Invocations);
        Assert.Equal(0, skipResult.FilesExtracted);
        Assert.Equal("pre-existing version", await File.ReadAllTextAsync(Path.Combine(destDirSkip, "doc.txt")));

        // Test Overwrite
        var destDirOverwrite = Path.Combine(_testDir, "zstd_dest_overwrite");
        Directory.CreateDirectory(destDirOverwrite);
        await File.WriteAllTextAsync(Path.Combine(destDirOverwrite, "doc.txt"), "pre-existing version");

        var overwriteResolver = new CallbackConflictResolver((_, _) => ValueTask.FromResult(FileConflictResolution.Overwrite));
        var overwriteReq = new ArchiveExtractionRequest(zrusPath, destDirOverwrite, Overwrite: false, ConflictResolver: overwriteResolver);
        var overwriteResult = await engine.ExtractAsync(overwriteReq);

        Assert.Single(overwriteResolver.Invocations);
        Assert.Equal(1, overwriteResult.FilesExtracted);
        Assert.Equal("archive version", await File.ReadAllTextAsync(Path.Combine(destDirOverwrite, "doc.txt")));
    }

    [Fact]
    public async Task ExtractAsync_UnifiedEngine_Zip_WithConflictResolver_HandlesSkipAndOverwrite()
    {
        var engine = new UnifiedArchiveEngine();
        var sourceDir = Path.Combine(_testDir, "zip_src");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "item.txt"), "zip archive data");

        var zipPath = Path.Combine(_testDir, "test.zip");
        await engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, zipPath, 3));

        // Test Skip
        var destDirSkip = Path.Combine(_testDir, "zip_dest_skip");
        Directory.CreateDirectory(destDirSkip);
        await File.WriteAllTextAsync(Path.Combine(destDirSkip, "item.txt"), "local file data");

        var skipResolver = new CallbackConflictResolver((_, _) => ValueTask.FromResult(FileConflictResolution.Skip));
        var skipReq = new ArchiveExtractionRequest(zipPath, destDirSkip, Overwrite: false, ConflictResolver: skipResolver);
        var skipResult = await engine.ExtractAsync(skipReq);

        Assert.Single(skipResolver.Invocations);
        Assert.Equal(0, skipResult.FilesExtracted);
        Assert.Equal("local file data", await File.ReadAllTextAsync(Path.Combine(destDirSkip, "item.txt")));

        // Test Overwrite
        var destDirOverwrite = Path.Combine(_testDir, "zip_dest_overwrite");
        Directory.CreateDirectory(destDirOverwrite);
        await File.WriteAllTextAsync(Path.Combine(destDirOverwrite, "item.txt"), "local file data");

        var overwriteResolver = new CallbackConflictResolver((_, _) => ValueTask.FromResult(FileConflictResolution.Overwrite));
        var overwriteReq = new ArchiveExtractionRequest(zipPath, destDirOverwrite, Overwrite: false, ConflictResolver: overwriteResolver);
        var overwriteResult = await engine.ExtractAsync(overwriteReq);

        Assert.Single(overwriteResolver.Invocations);
        Assert.Equal(1, overwriteResult.FilesExtracted);
        Assert.Equal("zip archive data", await File.ReadAllTextAsync(Path.Combine(destDirOverwrite, "item.txt")));
    }

    [Fact]
    public async Task ExtractAsync_TarGz_WithConflictResolver_HandlesSkip()
    {
        var engine = new UnifiedArchiveEngine();
        var tarGzPath = Path.Combine(_testDir, "test.tar.gz");
        await TestArchiveFixtures.CreateTarGzArchiveAsync(tarGzPath, new Dictionary<string, string> { ["entry.txt"] = "targz content" });

        var destDir = Path.Combine(_testDir, "targz_dest");
        Directory.CreateDirectory(destDir);
        await File.WriteAllTextAsync(Path.Combine(destDir, "entry.txt"), "existing targz file");

        var skipResolver = new CallbackConflictResolver((_, _) => ValueTask.FromResult(FileConflictResolution.Skip));
        var req = new ArchiveExtractionRequest(tarGzPath, destDir, Overwrite: false, ConflictResolver: skipResolver);
        var result = await engine.ExtractAsync(req);

        Assert.Single(skipResolver.Invocations);
        Assert.Equal(0, result.FilesExtracted);
        Assert.Equal("existing targz file", await File.ReadAllTextAsync(Path.Combine(destDir, "entry.txt")));
    }

    [Fact]
    public void FileConflictResolution_DefaultIsAbort_AndEnumValuesMatchContract()
    {
        Assert.Equal(FileConflictResolution.Abort, default(FileConflictResolution));
        Assert.Equal(0, (int)FileConflictResolution.Abort);
        Assert.Equal(1, (int)FileConflictResolution.Overwrite);
        Assert.Equal(2, (int)FileConflictResolution.OverwriteAll);
        Assert.Equal(3, (int)FileConflictResolution.Skip);
        Assert.Equal(4, (int)FileConflictResolution.SkipAll);
    }

    [Fact]
    public async Task FixedPolicyConflictResolver_StaticInstances_ReturnExpectedResolutions()
    {
        var dummyContext = new FileConflictContext(
            TargetPath: "/test/file.txt",
            RelativeEntryPath: "file.txt",
            EntryUncompressedSize: 100,
            EntryLastModified: null,
            ExistingFileSize: 200,
            ExistingLastModified: DateTimeOffset.UtcNow
        );

        Assert.Equal(FileConflictResolution.Abort, await FixedPolicyConflictResolver.Abort.ResolveConflictAsync(dummyContext));
        Assert.Equal(FileConflictResolution.Overwrite, await FixedPolicyConflictResolver.Overwrite.ResolveConflictAsync(dummyContext));
        Assert.Equal(FileConflictResolution.OverwriteAll, await FixedPolicyConflictResolver.OverwriteAll.ResolveConflictAsync(dummyContext));
        Assert.Equal(FileConflictResolution.Skip, await FixedPolicyConflictResolver.Skip.ResolveConflictAsync(dummyContext));
        Assert.Equal(FileConflictResolution.SkipAll, await FixedPolicyConflictResolver.SkipAll.ResolveConflictAsync(dummyContext));

        Assert.Equal(FileConflictResolution.Abort, FixedPolicyConflictResolver.Abort.Resolution);
        Assert.Equal(FileConflictResolution.OverwriteAll, FixedPolicyConflictResolver.OverwriteAll.Resolution);
        Assert.Equal(FileConflictResolution.SkipAll, FixedPolicyConflictResolver.SkipAll.Resolution);
    }

    [Fact]
    public async Task FixedPolicyConflictResolver_Abort_ThrowsOperationCanceledException_AndCleansUpCreatedFiles()
    {
        var targetDir = Path.Combine(_testDir, "fixed_abort_out");
        Directory.CreateDirectory(targetDir);
        var existingFile = Path.Combine(targetDir, "existing_file.txt");
        await File.WriteAllTextAsync(existingFile, "pre-existing content");

        var entries = new List<ExtractionEntry>
        {
            new("created_first.txt", false, 4, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("data"u8.ToArray()))),
            new("existing_file.txt", false, 3, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(
                source,
                targetDir,
                overwrite: false,
                totalBytes: 7,
                progress: null,
                conflictResolver: FixedPolicyConflictResolver.Abort));

        // Newly created file during session must be cleaned up
        Assert.False(File.Exists(Path.Combine(targetDir, "created_first.txt")));
        // Pre-existing file must remain untouched
        Assert.True(File.Exists(existingFile));
        Assert.Equal("pre-existing content", await File.ReadAllTextAsync(existingFile));
    }

    [Fact]
    public async Task FixedPolicyConflictResolver_OverwriteAll_OverwritesConflictingFiles()
    {
        var targetDir = Path.Combine(_testDir, "fixed_overwrite_all_out");
        Directory.CreateDirectory(targetDir);
        var existingFile = Path.Combine(targetDir, "existing_file.txt");
        await File.WriteAllTextAsync(existingFile, "old content");

        var entries = new List<ExtractionEntry>
        {
            new("existing_file.txt", false, 11, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new content"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);

        var result = await SafeArchiveExtractor.ExtractAllAsync(
            source,
            targetDir,
            overwrite: false,
            totalBytes: 11,
            progress: null,
            conflictResolver: FixedPolicyConflictResolver.OverwriteAll);

        Assert.Equal(1, result.FilesExtracted);
        Assert.Equal("new content", await File.ReadAllTextAsync(existingFile));
    }

    [Fact]
    public async Task FixedPolicyConflictResolver_SkipAll_PreservesConflictingFiles()
    {
        var targetDir = Path.Combine(_testDir, "fixed_skip_all_out");
        Directory.CreateDirectory(targetDir);
        var existingFile = Path.Combine(targetDir, "existing_file.txt");
        await File.WriteAllTextAsync(existingFile, "original content");

        var entries = new List<ExtractionEntry>
        {
            new("existing_file.txt", false, 3, null, null, _ => ValueTask.FromResult<Stream>(new MemoryStream("new"u8.ToArray())))
        };

        var source = new FakeExtractionSource(entries);

        var result = await SafeArchiveExtractor.ExtractAllAsync(
            source,
            targetDir,
            overwrite: false,
            totalBytes: 3,
            progress: null,
            conflictResolver: FixedPolicyConflictResolver.SkipAll);

        Assert.Equal(0, result.FilesExtracted);
        Assert.Equal("original content", await File.ReadAllTextAsync(existingFile));
    }
}

using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using RusZip.Desktop.Models;
using RusZip.Desktop.ViewModels;
using Xunit;

namespace RusZip.Desktop.Tests;

public class QuickExtractViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public QuickExtractViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ruszip_quick_extract_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* Ignore */ }
        }
    }

    private sealed class FakeArchiveEngine : IArchiveEngine
    {
        public ArchiveExtractionRequest? LastExtractionRequest { get; private set; }
        public Exception? ExceptionToThrow { get; set; }
        public ExtractionResult ResultToReturn { get; set; } = new(1024, 2, 2);
        public Action<IProgress<ProgressReport>?>? OnExtract { get; set; }

        public Task<ExtractionResult> ExtractAsync(ArchiveExtractionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            LastExtractionRequest = request;
            OnExtract?.Invoke(progress);
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ProgressReport(1024, 1024, "file2.txt", 100.0, 2, 2));
            return Task.FromResult(ResultToReturn);
        }

        public Task CompressAsync(ArchiveCompressionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<AppendResult> AppendAsync(ArchiveAppendRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    [Fact]
    public async Task StartExtractionAsync_ExtractHere_SetsDestinationToParentDirectory_AndEnablesConflictResolver()
    {
        var archiveFile = Path.Combine(_tempDir, "archive.zip");
        await File.WriteAllBytesAsync(archiveFile, [0x01, 0x02]);

        var engine = new FakeArchiveEngine();
        var options = new QuickExtractOptions(QuickExtractMode.ExtractHere, archiveFile);
        var vm = new QuickExtractViewModel(engine, options);

        await vm.StartExtractionAsync();

        Assert.NotNull(engine.LastExtractionRequest);
        Assert.Equal(archiveFile, engine.LastExtractionRequest.ArchivePath);
        Assert.Equal(_tempDir, engine.LastExtractionRequest.DestinationDirectory);
        Assert.Same(vm, engine.LastExtractionRequest.ConflictResolver);
        Assert.True(vm.IsSuccess);
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsError);
        Assert.Equal(2, vm.FilesExtracted);
        Assert.Equal(1024, vm.BytesExtracted);
        Assert.Equal("Extraction completed successfully.", vm.StatusMessage);
    }

    [Fact]
    public async Task StartExtractionAsync_ExtractTo_WithExplicitDestination_ExtractsToTarget()
    {
        var archiveFile = Path.Combine(_tempDir, "archive.zip");
        await File.WriteAllBytesAsync(archiveFile, [0x01, 0x02]);
        var targetDir = Path.Combine(_tempDir, "custom_dest");

        var engine = new FakeArchiveEngine();
        var options = new QuickExtractOptions(QuickExtractMode.ExtractTo, archiveFile, targetDir);
        var vm = new QuickExtractViewModel(engine, options);

        await vm.StartExtractionAsync();

        Assert.NotNull(engine.LastExtractionRequest);
        Assert.Equal(targetDir, engine.LastExtractionRequest.DestinationDirectory);
        Assert.Same(vm, engine.LastExtractionRequest.ConflictResolver);
        Assert.True(vm.IsSuccess);
    }

    [Fact]
    public async Task StartExtractionAsync_ExtractTo_WithoutDestination_PromptsFolderPicker_AndExtracts()
    {
        var archiveFile = Path.Combine(_tempDir, "archive.zip");
        await File.WriteAllBytesAsync(archiveFile, [0x01, 0x02]);
        var pickedDir = Path.Combine(_tempDir, "picked_folder");

        var engine = new FakeArchiveEngine();
        var options = new QuickExtractOptions(QuickExtractMode.ExtractTo, archiveFile);
        var vm = new QuickExtractViewModel(engine, options)
        {
            RequestFolderPicker = () => Task.FromResult<string?>(pickedDir)
        };

        await vm.StartExtractionAsync();

        Assert.NotNull(engine.LastExtractionRequest);
        Assert.Equal(pickedDir, engine.LastExtractionRequest.DestinationDirectory);
        Assert.True(vm.IsSuccess);
    }

    [Fact]
    public async Task StartExtractionAsync_ExtractTo_WhenFolderPickerCancelled_CancelsExtraction()
    {
        var archiveFile = Path.Combine(_tempDir, "archive.zip");
        await File.WriteAllBytesAsync(archiveFile, [0x01, 0x02]);

        var engine = new FakeArchiveEngine();
        var options = new QuickExtractOptions(QuickExtractMode.ExtractTo, archiveFile);
        var vm = new QuickExtractViewModel(engine, options)
        {
            RequestFolderPicker = () => Task.FromResult<string?>(null) // User cancelled picker
        };

        await vm.StartExtractionAsync();

        Assert.Null(engine.LastExtractionRequest);
        Assert.True(vm.IsCancelled);
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsSuccess);
        Assert.Equal("Extraction cancelled.", vm.StatusMessage);
    }

    [Fact]
    public async Task StartExtractionAsync_ExtractToDir_UsesExtractionPathResolver_ToDetermineUniqueDirectory_WithoutConflictResolver()
    {
        var archiveFile = Path.Combine(_tempDir, "sample.tar.gz");
        await File.WriteAllBytesAsync(archiveFile, [0x01, 0x02]);

        // Pre-create 'sample' directory so resolver increments to 'sample_2'
        var existingDir = Path.Combine(_tempDir, "sample");
        Directory.CreateDirectory(existingDir);

        var engine = new FakeArchiveEngine();
        var options = new QuickExtractOptions(QuickExtractMode.ExtractToDir, archiveFile);
        var vm = new QuickExtractViewModel(engine, options);

        await vm.StartExtractionAsync();

        var expectedDir = Path.Combine(_tempDir, "sample_2");
        Assert.NotNull(engine.LastExtractionRequest);
        Assert.Equal(expectedDir, engine.LastExtractionRequest.DestinationDirectory);
        Assert.Null(engine.LastExtractionRequest.ConflictResolver);
        Assert.True(vm.IsSuccess);
    }

    [Fact]
    public async Task StartExtractionAsync_NonExistentArchive_SetsErrorState()
    {
        var nonExistentPath = Path.Combine(_tempDir, "does_not_exist.zip");
        var engine = new FakeArchiveEngine();
        var options = new QuickExtractOptions(QuickExtractMode.ExtractHere, nonExistentPath);
        var vm = new QuickExtractViewModel(engine, options);

        await vm.StartExtractionAsync();

        Assert.Null(engine.LastExtractionRequest);
        Assert.True(vm.IsError);
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsSuccess);
        Assert.Contains("Archive file not found", vm.ErrorMessage);
    }

    [Fact]
    public async Task StartExtractionAsync_EngineException_SetsErrorState()
    {
        var archiveFile = Path.Combine(_tempDir, "archive.zip");
        await File.WriteAllBytesAsync(archiveFile, [0x01]);

        var engine = new FakeArchiveEngine
        {
            ExceptionToThrow = new InvalidOperationException("Corrupt header")
        };
        var options = new QuickExtractOptions(QuickExtractMode.ExtractHere, archiveFile);
        var vm = new QuickExtractViewModel(engine, options);

        await vm.StartExtractionAsync();

        Assert.True(vm.IsError);
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsSuccess);
        Assert.Equal("Corrupt header", vm.ErrorMessage);
        Assert.Contains("Extraction failed", vm.StatusMessage);
    }

    [Fact]
    public void ReportProgress_UpdatesProgressPropertiesAndSpeedEta()
    {
        var engine = new FakeArchiveEngine();
        var vm = new QuickExtractViewModel(engine);

        var report = new ProgressReport(
            ProcessedBytes: 500,
            TotalBytes: 1000,
            CurrentFileName: "my-file.txt",
            Percentage: 50.0,
            ProcessedFiles: 1,
            IsIndeterminate: false
        );

        vm.ReportProgress(report);

        Assert.Equal("my-file.txt", vm.CurrentFileName);
        Assert.Equal(50.0, vm.ProgressPercentage);
        Assert.False(vm.IsIndeterminate);
        Assert.Equal(500, vm.ProcessedBytes);
        Assert.Equal(1000, vm.TotalBytes);
        Assert.Equal(1, vm.ProcessedFiles);
        Assert.NotEmpty(vm.BytesProgressFormatted);
        Assert.NotNull(vm.FormattedSpeed);
        Assert.NotNull(vm.FormattedEta);
    }

    [Fact]
    public async Task ResolveConflictAsync_CallsRequestConflictResolution_WhenConfigured()
    {
        var engine = new FakeArchiveEngine();
        var vm = new QuickExtractViewModel(engine);

        var context = new FileConflictContext(
            TargetPath: "/test/file.txt",
            RelativeEntryPath: "file.txt",
            EntryUncompressedSize: 500,
            EntryLastModified: null,
            ExistingFileSize: 600,
            ExistingLastModified: DateTimeOffset.UtcNow
        );

        FileConflictContext? receivedContext = null;
        vm.RequestConflictResolution = ctx =>
        {
            receivedContext = ctx;
            return Task.FromResult(FileConflictResolution.SkipAll);
        };

        var resolution = await vm.ResolveConflictAsync(context);

        Assert.Same(context, receivedContext);
        Assert.Equal(FileConflictResolution.SkipAll, resolution);
    }

    [Fact]
    public async Task ResolveConflictAsync_DefaultsToOverwrite_WhenResolverNotConfigured()
    {
        var engine = new FakeArchiveEngine();
        var vm = new QuickExtractViewModel(engine);

        var context = new FileConflictContext(
            TargetPath: "/test/file.txt",
            RelativeEntryPath: "file.txt",
            EntryUncompressedSize: 500,
            EntryLastModified: null,
            ExistingFileSize: 600,
            ExistingLastModified: DateTimeOffset.UtcNow
        );

        var resolution = await vm.ResolveConflictAsync(context);

        Assert.Equal(FileConflictResolution.Overwrite, resolution);
    }

    [Fact]
    public async Task CancelCommand_TriggersCancellationToken_AndSetsCancelledState()
    {
        var archiveFile = Path.Combine(_tempDir, "archive.zip");
        await File.WriteAllBytesAsync(archiveFile, [0x01]);

        var engine = new FakeArchiveEngine();
        var options = new QuickExtractOptions(QuickExtractMode.ExtractHere, archiveFile);
        var vm = new QuickExtractViewModel(engine, options);

        engine.OnExtract = _ =>
        {
            vm.CancelCommand.Execute(null);
        };

        await vm.StartExtractionAsync();

        Assert.True(vm.IsCancelled);
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsSuccess);
        Assert.Equal("Extraction cancelled.", vm.StatusMessage);
    }

    [Fact]
    public async Task AutoCloseCountdown_CountsDownAndInvokesRequestClose()
    {
        var engine = new FakeArchiveEngine();
        var vm = new QuickExtractViewModel(engine)
        {
            DelayAsync = (_, _) => Task.CompletedTask // Fast delay for test
        };

        bool closeRequested = false;
        vm.RequestClose = () => closeRequested = true;

        vm.StartAutoCloseCountdown(3);

        // Allow fast task loop to finish
        await Task.Delay(50);

        Assert.True(closeRequested);
        Assert.False(vm.IsAutoCloseActive);
        Assert.Equal(0, vm.AutoCloseRemainingSeconds);
    }

    [Fact]
    public async Task CancelAutoCloseCountdown_StopsAutoClose()
    {
        var engine = new FakeArchiveEngine();
        var tcs = new TaskCompletionSource();
        var vm = new QuickExtractViewModel(engine)
        {
            DelayAsync = async (_, ct) =>
            {
                await Task.Delay(500, ct);
            }
        };

        bool closeRequested = false;
        vm.RequestClose = () => closeRequested = true;

        vm.StartAutoCloseCountdown(3);
        Assert.True(vm.IsAutoCloseActive);

        vm.CancelAutoCloseCountdown();
        Assert.False(vm.IsAutoCloseActive);

        await Task.Delay(50);
        Assert.False(closeRequested);
    }

    [Fact]
    public async Task OpenFolderCommand_CancelsAutoClose_AndInvokesOpenFolderHandler()
    {
        var engine = new FakeArchiveEngine();
        var targetDir = Path.Combine(_tempDir, "extracted");
        Directory.CreateDirectory(targetDir);

        var vm = new QuickExtractViewModel(engine)
        {
            DestinationDirectory = targetDir
        };

        string? openedFolder = null;
        vm.OpenFolderHandler = folder =>
        {
            openedFolder = folder;
            return Task.CompletedTask;
        };

        vm.StartAutoCloseCountdown(3);
        Assert.True(vm.IsAutoCloseActive);

        await vm.OpenFolderCommand.ExecuteAsync(null);

        Assert.False(vm.IsAutoCloseActive);
        Assert.Equal(targetDir, openedFolder);
    }

    [Fact]
    public void CloseCommand_CancelsAutoClose_AndInvokesRequestClose()
    {
        var engine = new FakeArchiveEngine();
        var vm = new QuickExtractViewModel(engine);

        bool closeRequested = false;
        vm.RequestClose = () => closeRequested = true;

        vm.StartAutoCloseCountdown(3);
        Assert.True(vm.IsAutoCloseActive);

        vm.CloseCommand.Execute(null);

        Assert.False(vm.IsAutoCloseActive);
        Assert.True(closeRequested);
    }

    [Fact]
    public async Task StartExtractionAsync_WithRealEngine_ExtractsFilesAndPopulatesMetrics()
    {
        var srcDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "doc1.txt"), "Hello world");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "doc2.txt"), "Second file");

        var zipPath = Path.Combine(_tempDir, "test.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(srcDir, zipPath);

        var destDir = Path.Combine(_tempDir, "dest");
        var engine = new RusZip.Core.Engines.UnifiedArchiveEngine();
        var options = new QuickExtractOptions(QuickExtractMode.ExtractTo, zipPath, destDir);
        var vm = new QuickExtractViewModel(engine, options);

        await vm.StartExtractionAsync();

        Assert.True(vm.IsSuccess);
        Assert.False(vm.IsRunning);
        Assert.True(File.Exists(Path.Combine(destDir, "doc1.txt")));
        Assert.True(File.Exists(Path.Combine(destDir, "doc2.txt")));
        Assert.Equal("Hello world", await File.ReadAllTextAsync(Path.Combine(destDir, "doc1.txt")));
        Assert.Equal(2, vm.FilesExtracted);
        Assert.True(vm.BytesExtracted > 0);
    }

    [Fact]
    public async Task StartExtractionAsync_WithRealEngine_ConflictResolution_Skip_PreservesExistingFile()
    {
        var srcDir = Path.Combine(_tempDir, "src_skip");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "conflict.txt"), "New Content From Archive");

        var zipPath = Path.Combine(_tempDir, "conflict.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(srcDir, zipPath);

        var destDir = Path.Combine(_tempDir, "dest_skip");
        Directory.CreateDirectory(destDir);
        await File.WriteAllTextAsync(Path.Combine(destDir, "conflict.txt"), "Existing Original Content");

        var engine = new RusZip.Core.Engines.UnifiedArchiveEngine();
        var options = new QuickExtractOptions(QuickExtractMode.ExtractTo, zipPath, destDir);
        var vm = new QuickExtractViewModel(engine, options)
        {
            RequestConflictResolution = ctx => Task.FromResult(FileConflictResolution.Skip)
        };

        await vm.StartExtractionAsync();

        Assert.True(vm.IsSuccess);
        Assert.Equal("Existing Original Content", await File.ReadAllTextAsync(Path.Combine(destDir, "conflict.txt")));
    }

    [Fact]
    public async Task StartExtractionAsync_WithRealEngine_ConflictResolution_Overwrite_UpdatesFile()
    {
        var srcDir = Path.Combine(_tempDir, "src_ow");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "conflict.txt"), "New Content From Archive");

        var zipPath = Path.Combine(_tempDir, "conflict_ow.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(srcDir, zipPath);

        var destDir = Path.Combine(_tempDir, "dest_ow");
        Directory.CreateDirectory(destDir);
        await File.WriteAllTextAsync(Path.Combine(destDir, "conflict.txt"), "Existing Original Content");

        var engine = new RusZip.Core.Engines.UnifiedArchiveEngine();
        var options = new QuickExtractOptions(QuickExtractMode.ExtractTo, zipPath, destDir);
        var vm = new QuickExtractViewModel(engine, options)
        {
            RequestConflictResolution = ctx => Task.FromResult(FileConflictResolution.Overwrite)
        };

        await vm.StartExtractionAsync();

        Assert.True(vm.IsSuccess);
        Assert.Equal("New Content From Archive", await File.ReadAllTextAsync(Path.Combine(destDir, "conflict.txt")));
    }
}

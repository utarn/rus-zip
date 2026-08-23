using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Tests;

public class MainWindowEmptyStateTests
{
    private class FakeArchiveEngine : IArchiveEngine
    {
        public List<ArchiveEntry> EntriesToReturn { get; set; } = [];
        public ArchiveCompressionRequest? LastCompressionRequest { get; private set; }
        public ArchiveExtractionRequest? LastExtractionRequest { get; private set; }

        public Task CompressAsync(ArchiveCompressionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            LastCompressionRequest = request;
            return Task.CompletedTask;
        }

        public Task<AppendResult> AppendAsync(ArchiveAppendRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            return Task.FromResult(new AppendResult(true, request.ArchivePath, "zrus", 0, 0, 0, 0, 0, 0, 0, 1.0, 0));
        }

        public Task<ArchiveDeleteResult> DeleteEntriesAsync(ArchiveDeleteRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            return Task.FromResult(new ArchiveDeleteResult(true, request.ArchivePath, 0, 0, 0, 0, 0));
        }

        public Task<ExtractionResult> ExtractAsync(ArchiveExtractionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            LastExtractionRequest = request;
            return Task.FromResult(new ExtractionResult(0, 0, 0));
        }

        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<ArchiveEntry>>(EntriesToReturn);
        }
    }

    [Fact]
    public void EmptyState_InitiallyActiveWhenNoArchiveLoaded()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);

        Assert.False(vm.HasOpenArchive);
        Assert.False(vm.IsDragOver);
        Assert.False(vm.IsCompressDialogVisible);
        Assert.False(vm.Progress.IsOperationRunning);
    }

    [Fact]
    public void DragOverState_UpdatesVisualFeedbackProperty()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);

        vm.IsDragOver = true;
        Assert.True(vm.IsDragOver);

        vm.IsDragOver = false;
        Assert.False(vm.IsDragOver);
    }

    [Fact]
    public void CreateArchiveChip_TriggersCompressionDialog()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);

        vm.CreateArchiveCommand.Execute(null);

        Assert.True(vm.IsCompressDialogVisible);
        Assert.Equal(9, vm.Settings.CompressionLevel);
        Assert.Equal("Balanced", vm.Settings.ActivePreset);
    }

    [Fact]
    public async Task OpenArchiveChip_WithPickerCallback_OpensArchive()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn = [new ArchiveEntry("file1.txt", 100, 50, null, false)]
            };
            var vm = new MainWindowViewModel(fakeEngine)
            {
                RequestOpenArchivePicker = () => Task.FromResult<string?>(tempFile)
            };

            await vm.OpenArchivePickerCommand.ExecuteAsync(null);

            Assert.True(vm.HasOpenArchive);
            Assert.Equal(tempFile, vm.Browser.LoadedArchivePath);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DroppingArchiveInEmptyState_OpensArchiveDirectly()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zrus");
        await File.WriteAllTextAsync(tempFile, "archive content");
        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn = [new ArchiveEntry("file1.txt", 100, 50, null, false)]
            };
            var vm = new MainWindowViewModel(fakeEngine);

            await vm.HandleDroppedPathsAsync([tempFile]);

            Assert.True(vm.HasOpenArchive);
            Assert.Equal(tempFile, vm.Browser.LoadedArchivePath);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DroppingFilesToCompressInEmptyState_OpensCompressionDialog()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(tempFile, "hello");
        try
        {
            var fakeEngine = new FakeArchiveEngine();
            var vm = new MainWindowViewModel(fakeEngine);

            await vm.HandleDroppedPathsAsync([tempFile]);

            Assert.False(vm.HasOpenArchive);
            Assert.True(vm.IsCompressDialogVisible);
            Assert.Equal(tempFile, vm.Settings.SourcePath);
            Assert.Equal(tempFile + ".zrus", vm.Settings.DestinationPath);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}

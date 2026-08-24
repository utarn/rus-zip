using Avalonia.Headless.XUnit;
using RusZip.Core.Abstractions;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using RusZip.Desktop.Models;
using RusZip.Desktop.Services;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class ArchiveBrowserPreviewInteractionTests : IDisposable
{
    private readonly string _tempDirectory;

    public ArchiveBrowserPreviewInteractionTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ruszip-preview-interaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { /* Ignored */ }
        }
    }

    [Fact]
    public async Task ActivateItemAsync_OnFileEntry_TriggersPreviewItemRequested()
    {
        var browserVm = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("docs/readme.txt", 100, 50, DateTime.UtcNow, false),
            new("docs/", 0, 0, DateTime.UtcNow, true)
        };

        browserVm.LoadEntries("test.zip", entries);

        ArchiveItemViewModel? previewedItem = null;
        browserVm.PreviewItemRequested += item =>
        {
            previewedItem = item;
            return Task.CompletedTask;
        };

        var fileItem = browserVm.RootItems[0].Children.First(c => !c.IsDirectory);
        await browserVm.ActivateItemAsync(fileItem);

        Assert.NotNull(previewedItem);
        Assert.Equal("readme.txt", previewedItem.Name);
    }

    [Fact]
    public async Task ActivateItemAsync_OnDirectoryEntry_TogglesExpandedStateWithoutPreview()
    {
        var browserVm = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("folder/sub/file.txt", 100, 50, DateTime.UtcNow, false),
            new("folder/", 0, 0, DateTime.UtcNow, true)
        };

        browserVm.LoadEntries("test.zip", entries);

        bool previewFired = false;
        browserVm.PreviewItemRequested += _ =>
        {
            previewFired = true;
            return Task.CompletedTask;
        };

        var dirItem = browserVm.RootItems[0];
        Assert.True(dirItem.IsDirectory);
        Assert.False(dirItem.IsExpanded);

        // First activation expands
        await browserVm.ActivateItemAsync(dirItem);
        Assert.True(dirItem.IsExpanded);
        Assert.False(previewFired);

        // Second activation collapses
        await browserVm.ActivateItemAsync(dirItem);
        Assert.False(dirItem.IsExpanded);
        Assert.False(previewFired);
    }

    [Fact]
    public async Task MainWindowViewModel_PreviewRequested_CallsPreviewServiceAndUpdatesStatus()
    {
        var sampleDir = Path.Combine(_tempDirectory, "sample");
        Directory.CreateDirectory(sampleDir);
        var originalFile = Path.Combine(sampleDir, "test.txt");
        await File.WriteAllTextAsync(originalFile, "hello world");

        var zrusPath = Path.Combine(_tempDirectory, "archive.zrus");
        var engine = new UnifiedArchiveEngine();
        await engine.CompressAsync(new ArchiveCompressionRequest([originalFile], zrusPath, 3));

        string? launchedPath = null;
        var previewService = new ArchivePreviewService(engine, path =>
        {
            launchedPath = path;
            return null;
        });

        var associationService = FileAssociationServiceFactory.CreateDefault();
        var recentService = new JsonRecentArchivesService(Path.Combine(_tempDirectory, "recent.json"));
        var vm = new MainWindowViewModel(engine, associationService, recentService, previewService);

        await vm.OpenArchiveAsync(zrusPath);

        var fileItem = vm.Browser.RootItems.First(c => !c.IsDirectory);
        await vm.Browser.ActivateItemAsync(fileItem);

        Assert.NotNull(launchedPath);
        Assert.True(File.Exists(launchedPath));
        Assert.Contains("Previewing test.txt", vm.StatusText);

        // Closing archive cleans up preview directories
        vm.CloseArchiveCommand.Execute(null);
        Assert.False(vm.HasOpenArchive);
        Assert.Empty(previewService.ActivePreviewDirectories);
    }
}

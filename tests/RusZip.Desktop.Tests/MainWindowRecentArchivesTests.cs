using Avalonia.Controls;
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

public sealed class MainWindowRecentArchivesTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _storageFile;
    private readonly JsonRecentArchivesService _recentService;

    public MainWindowRecentArchivesTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ruszip-mru-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _storageFile = Path.Combine(_tempDirectory, "recent-archives.json");
        _recentService = new JsonRecentArchivesService(_storageFile);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignored
            }
        }
    }

    [Fact]
    public async Task OpenArchiveAsync_AddsPathToRecentArchives()
    {
        var dummyArchive = Path.Combine(_tempDirectory, "test.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(dummyArchive, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("file.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("test content");
        }

        var engine = new UnifiedArchiveEngine();
        var associationService = FileAssociationServiceFactory.CreateDefault();
        var vm = new MainWindowViewModel(engine, associationService, _recentService);

        Assert.False(vm.HasRecentArchives);
        Assert.Empty(vm.RecentArchives);

        await vm.OpenArchiveAsync(dummyArchive);

        Assert.True(vm.HasRecentArchives);
        Assert.Single(vm.RecentArchives);
        Assert.Equal(Path.GetFullPath(dummyArchive), vm.RecentArchives[0].FullPath);
        Assert.Equal("test.zip", vm.RecentArchives[0].FileName);
        Assert.Equal("ZIP", vm.RecentArchives[0].ExtensionBadge);
    }

    [Fact]
    public async Task OpenRecentArchiveAsync_WhenFileMissing_PrunesStalePathGracefully()
    {
        var missingPath = Path.Combine(_tempDirectory, "deleted.zrus");
        await _recentService.AddRecentPathAsync(missingPath);

        var engine = new UnifiedArchiveEngine();
        var associationService = FileAssociationServiceFactory.CreateDefault();
        var vm = new MainWindowViewModel(engine, associationService, _recentService);
        vm.SyncRecentArchives();

        Assert.True(vm.HasRecentArchives);
        Assert.Single(vm.RecentArchives);

        await vm.OpenRecentArchiveAsync(missingPath);

        Assert.False(vm.HasRecentArchives);
        Assert.Empty(vm.RecentArchives);
        Assert.Contains("Recent archive not found", vm.StatusText);
    }

    [Fact]
    public async Task ClearRecentArchivesAsync_ClearsListAndUpdatesHasRecentArchives()
    {
        var path1 = Path.Combine(_tempDirectory, "archive1.zip");
        var path2 = Path.Combine(_tempDirectory, "archive2.zrus");
        await _recentService.AddRecentPathAsync(path1);
        await _recentService.AddRecentPathAsync(path2);

        var engine = new UnifiedArchiveEngine();
        var associationService = FileAssociationServiceFactory.CreateDefault();
        var vm = new MainWindowViewModel(engine, associationService, _recentService);
        vm.SyncRecentArchives();

        Assert.Equal(2, vm.RecentArchives.Count);
        Assert.True(vm.HasRecentArchives);

        await vm.ClearRecentArchivesAsync();

        Assert.Empty(vm.RecentArchives);
        Assert.False(vm.HasRecentArchives);
        Assert.Contains("cleared", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveRecentArchiveAsync_RemovesSingleItem()
    {
        var path1 = Path.Combine(_tempDirectory, "archive1.zip");
        var path2 = Path.Combine(_tempDirectory, "archive2.zrus");
        await _recentService.AddRecentPathAsync(path1);
        await _recentService.AddRecentPathAsync(path2);

        var engine = new UnifiedArchiveEngine();
        var associationService = FileAssociationServiceFactory.CreateDefault();
        var vm = new MainWindowViewModel(engine, associationService, _recentService);
        vm.SyncRecentArchives();

        Assert.Equal(2, vm.RecentArchives.Count);

        await vm.RemoveRecentArchiveAsync(path1);

        Assert.Single(vm.RecentArchives);
        Assert.Equal(Path.GetFullPath(path2), vm.RecentArchives[0].FullPath);
    }

    [AvaloniaFact]
    public async Task MainWindow_WithRecentArchives_BindsToRecentMenuAndLandingCard()
    {
        var path = Path.Combine(_tempDirectory, "sample.zip");
        await _recentService.AddRecentPathAsync(path);

        var engine = new UnifiedArchiveEngine();
        var associationService = FileAssociationServiceFactory.CreateDefault();
        var vm = new MainWindowViewModel(engine, associationService, _recentService);
        vm.SyncRecentArchives();

        var window = new MainWindow
        {
            DataContext = vm
        };

        var menu = window.FindControl<Menu>("AppMenuBar");
        Assert.NotNull(menu);

        var fileMenuItem = menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header?.ToString()?.Contains("File") == true);
        Assert.NotNull(fileMenuItem);

        var clearMenuItem = fileMenuItem.Items.OfType<MenuItem>().FirstOrDefault(m => m.Header?.ToString()?.Contains("Clear Recent Archives") == true);
        Assert.NotNull(clearMenuItem);
        Assert.True(clearMenuItem.IsEnabled);
    }
}

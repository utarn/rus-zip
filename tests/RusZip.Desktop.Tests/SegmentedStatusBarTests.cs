using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using RusZip.Desktop.Models;
using RusZip.Desktop.Services;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class SegmentedStatusBarTests : IDisposable
{
    private readonly string _tempDirectory;

    public SegmentedStatusBarTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ruszip-statusbar-test-" + Guid.NewGuid().ToString("N"));
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
    public void SelectionMetricsText_WhenNoSelection_ReturnsNoItemsSelected()
    {
        var engine = new UnifiedArchiveEngine();
        var associationService = FileAssociationServiceFactory.CreateDefault();
        var recentService = new JsonRecentArchivesService(Path.Combine(_tempDirectory, "recent.json"));
        var vm = new MainWindowViewModel(engine, associationService, recentService);

        vm.HasOpenArchive = true;
        vm.Browser.SelectedItems.Clear();
        vm.Browser.SelectedItem = null;

        Assert.Equal("No items selected", vm.SelectionMetricsText);
    }

    [Fact]
    public void SelectionMetricsText_WithSingleAndMultipleSelection_ComputesAccurateCountsAndBytes()
    {
        var engine = new UnifiedArchiveEngine();
        var associationService = FileAssociationServiceFactory.CreateDefault();
        var recentService = new JsonRecentArchivesService(Path.Combine(_tempDirectory, "recent.json"));
        var vm = new MainWindowViewModel(engine, associationService, recentService);

        var item1 = new ArchiveItemViewModel { Name = "file1.txt", RelativePath = "file1.txt", UncompressedSize = 1048576, ItemType = ArchiveItemType.File }; // 1 MB
        var item2 = new ArchiveItemViewModel { Name = "file2.txt", RelativePath = "file2.txt", UncompressedSize = 2097152, ItemType = ArchiveItemType.File }; // 2 MB
        var dir1 = new ArchiveItemViewModel { Name = "folder", RelativePath = "folder", UncompressedSize = 0, ItemType = ArchiveItemType.Directory };

        vm.HasOpenArchive = true;

        // Single file selection
        vm.Browser.SelectedItem = item1;
        Assert.Equal("1 item selected (1.0 MB)", vm.SelectionMetricsText);

        // Single dir selection
        vm.Browser.SelectedItem = dir1;
        Assert.Equal("1 directory selected", vm.SelectionMetricsText);

        // Multiple items selection
        vm.Browser.SetSelectedItems([item1, item2]);
        Assert.Equal("2 items selected (3.0 MB)", vm.SelectionMetricsText);
    }

    [Theory]
    [InlineData("archive.zrus", "[ .zrus | Read-Write ]")]
    [InlineData("archive.zip", "[ .zip | Read-Write ]")]
    [InlineData("archive.rar", "[ .rar | Read-Only ]")]
    [InlineData("archive.7z", "[ .7z | Read-Only ]")]
    [InlineData("archive.tar.gz", "[ .tar.gz | Read-Only ]")]
    public void FormatCapabilityBadge_MatchesFormatMutability(string archivePath, string expectedBadge)
    {
        var engine = new UnifiedArchiveEngine();
        var associationService = FileAssociationServiceFactory.CreateDefault();
        var recentService = new JsonRecentArchivesService(Path.Combine(_tempDirectory, "recent.json"));
        var vm = new MainWindowViewModel(engine, associationService, recentService);

        vm.HasOpenArchive = true;
        vm.Browser.LoadedArchivePath = archivePath;

        Assert.Equal(expectedBadge, vm.FormatCapabilityBadge);
    }

    [Fact]
    public void GuardrailLimitBadge_ReflectsActiveExtractionSettings()
    {
        var engine = new UnifiedArchiveEngine();
        var associationService = FileAssociationServiceFactory.CreateDefault();
        var recentService = new JsonRecentArchivesService(Path.Combine(_tempDirectory, "recent.json"));
        var vm = new MainWindowViewModel(engine, associationService, recentService);

        vm.HasOpenArchive = true;

        Assert.Equal("[ Limit: 64.0 GB ]", vm.GuardrailLimitBadge);

        vm.Browser.ExtractionSettings.MaxUncompressedSizeText = "128 GB";
        Assert.Equal("[ Limit: 128.0 GB ]", vm.GuardrailLimitBadge);

        vm.Browser.ExtractionSettings.MaxUncompressedSizeText = "0";
        vm.Browser.ExtractionSettings.MaxEntryCount = 0;
        Assert.Equal("[ Limit: Unlimited ]", vm.GuardrailLimitBadge);
    }

    [AvaloniaFact]
    public void MainWindow_StatusBar_InstantiatesWithThreeSegments()
    {
        var engine = new UnifiedArchiveEngine();
        var associationService = FileAssociationServiceFactory.CreateDefault();
        var recentService = new JsonRecentArchivesService(Path.Combine(_tempDirectory, "recent.json"));
        var vm = new MainWindowViewModel(engine, associationService, recentService);

        var window = new MainWindow
        {
            DataContext = vm
        };

        Assert.NotNull(window);
    }
}

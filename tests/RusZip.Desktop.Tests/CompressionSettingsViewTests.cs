using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;

namespace RusZip.Desktop.Tests;

public class CompressionSettingsViewTests
{
    [AvaloniaFact]
    public void PresetCards_ItemsControl_BindsToPresetProfiles()
    {
        var vm = new CompressionSettingsViewModel();
        var view = new CompressionSettingsView { DataContext = vm };

        var cards = view.FindControl<ItemsControl>("PresetCards");
        Assert.NotNull(cards);
        Assert.Equal(4, cards.Items.Count);

        for (int i = 0; i < vm.Presets.Count; i++)
        {
            Assert.Same(vm.Presets[i], cards.Items[i]);
        }
    }

    [AvaloniaFact]
    public void MultiSourceDisplay_ShowsStagedSources_WhenMultiplePathsStaged()
    {
        var vm = new CompressionSettingsViewModel();
        vm.StageSources(["/tmp/a.txt", "/tmp/b.txt", "/tmp/c.txt"]);

        var view = new CompressionSettingsView { DataContext = vm };

        Assert.True(vm.HasMultipleSources);
        Assert.Equal("/tmp/a.txt", vm.SourcePath);
        Assert.Contains("/tmp/a.txt", vm.SourcePathsDisplay);
        Assert.Contains("/tmp/c.txt", vm.SourcePathsDisplay);

        var stagedGrid = view.FindControl<DataGrid>("StagedGrid");
        Assert.NotNull(stagedGrid);
        Assert.Equal(3, vm.StagedItems.Count);
        Assert.NotNull(vm.GridSource);
    }

    [AvaloniaFact]
    public void StagingSingleSource_DoesNotMarkAsMultiple()
    {
        var vm = new CompressionSettingsViewModel();
        vm.StageSources(["/tmp/single.txt"]);

        Assert.False(vm.HasMultipleSources);
        Assert.Single(vm.SourcePaths);
        Assert.Equal("/tmp/single.txt", vm.SourcePath);
        Assert.Equal("/tmp/single.txt.zrus", vm.DestinationPath);
    }

    [Fact]
    public void CreateSaveFilePickerOptions_ForZrus_RestrictsChoicesToZrusAndAllFiles()
    {
        var vm = new CompressionSettingsViewModel
        {
            SourcePath = "/path/to/myfolder",
            SelectedFormat = ".zrus"
        };

        var options = CompressionSettingsView.CreateSaveFilePickerOptions(vm);

        Assert.Equal("Save Archive As", options.Title);
        Assert.Equal("zrus", options.DefaultExtension);
        Assert.Equal("myfolder.zrus", options.SuggestedFileName);
        Assert.NotNull(options.FileTypeChoices);
        Assert.Equal(2, options.FileTypeChoices.Count);
        Assert.Equal("ZRUS Archive (*.zrus)", options.FileTypeChoices[0].Name);
        Assert.Equal(["*.zrus"], options.FileTypeChoices[0].Patterns);
        Assert.Equal("All Files (*.*)", options.FileTypeChoices[1].Name);
        Assert.Equal(["*.*"], options.FileTypeChoices[1].Patterns);
    }

    [Fact]
    public void CreateSaveFilePickerOptions_ForZip_RestrictsChoicesToZipAndAllFiles()
    {
        var vm = new CompressionSettingsViewModel
        {
            SourcePath = "/path/to/myfolder",
            SelectedFormat = ".zip"
        };

        var options = CompressionSettingsView.CreateSaveFilePickerOptions(vm);

        Assert.Equal("Save Archive As", options.Title);
        Assert.Equal("zip", options.DefaultExtension);
        Assert.Equal("myfolder.zip", options.SuggestedFileName);
        Assert.NotNull(options.FileTypeChoices);
        Assert.Equal(2, options.FileTypeChoices.Count);
        Assert.Equal("ZIP Archive (*.zip)", options.FileTypeChoices[0].Name);
        Assert.Equal(["*.zip"], options.FileTypeChoices[0].Patterns);
        Assert.Equal("All Files (*.*)", options.FileTypeChoices[1].Name);
        Assert.Equal(["*.*"], options.FileTypeChoices[1].Patterns);
    }
}

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

        // The ItemsControl showing staged sources resolves to the three paths.
        var stagedItems = view.FindControl<ItemsControl>("StagedSources");
        Assert.NotNull(stagedItems);
        Assert.Equal(3, stagedItems.Items.Count);
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
}

using System.ComponentModel;
using RusZip.Desktop.Models;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Tests;

public class CompressionSettingsViewModelTests
{
    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        var vm = new CompressionSettingsViewModel();

        Assert.Equal(9, vm.CompressionLevel);
        Assert.Equal("Balanced", vm.ProfileName);
        Assert.Equal("#0078D4", vm.ProfileBadgeColor);
        Assert.Equal(".zrus", vm.SelectedFormat);
        Assert.Contains(".zrus", vm.Formats);
        Assert.Contains(".zip", vm.Formats);
        Assert.Equal(string.Empty, vm.SourcePath);
        Assert.Equal(string.Empty, vm.DestinationPath);
        Assert.Contains("Level 6–11", vm.ProfileDescription);
        Assert.Equal("Balanced", vm.ActivePreset);
        Assert.True(vm.IsBalancedSelected);
        Assert.False(vm.IsFastSelected);
        Assert.False(vm.IsHighSelected);
        Assert.False(vm.IsUltraSelected);
        Assert.False(vm.IsCustomSelected);
        Assert.Equal("~65%", vm.CurrentRatioEstimate);
        Assert.Equal("Balanced", vm.CurrentThroughputEstimate);
    }

    [Fact]
    public void Presets_ContainsFourSegmentedProfiles()
    {
        var vm = new CompressionSettingsViewModel();

        Assert.Equal(4, vm.Presets.Count);

        var fast = vm.Presets[0];
        Assert.Equal(3, fast.Level);
        Assert.Equal("Fast", fast.Name);
        Assert.Equal("~50%", fast.Ratio);
        Assert.Equal("Fastest", fast.Throughput);
        Assert.Equal("#28A745", fast.BadgeColor);

        var balanced = vm.Presets[1];
        Assert.Equal(9, balanced.Level);
        Assert.Equal("Balanced", balanced.Name);
        Assert.Equal("~65%", balanced.Ratio);
        Assert.Equal("Balanced", balanced.Throughput);
        Assert.Equal("#0078D4", balanced.BadgeColor);

        var high = vm.Presets[2];
        Assert.Equal(15, high.Level);
        Assert.Equal("High", high.Name);
        Assert.Equal("~75%", high.Ratio);
        Assert.Equal("High Ratio", high.Throughput);
        Assert.Equal("#E67E22", high.BadgeColor);

        var ultra = vm.Presets[3];
        Assert.Equal(22, ultra.Level);
        Assert.Equal("Ultra", ultra.Name);
        Assert.Equal("~80%", ultra.Ratio);
        Assert.Equal("Maximum", ultra.Throughput);
        Assert.Equal("#D83B01", ultra.BadgeColor);
    }

    [Theory]
    [InlineData(3, "Fast", "#28A745", "Level 1–5")]
    [InlineData(9, "Balanced", "#0078D4", "Level 6–11")]
    [InlineData(15, "High", "#E67E22", "Level 12–18")]
    [InlineData(22, "Ultra", "#D83B01", "Level 19–22")]
    public void SetPresetCommand_SetsExpectedLevelAndBadge(int presetLevel, string expectedName, string expectedColor, string expectedDescPrefix)
    {
        var vm = new CompressionSettingsViewModel();

        vm.SetPresetCommand.Execute(presetLevel);

        Assert.Equal(presetLevel, vm.CompressionLevel);
        Assert.Equal(expectedName, vm.ProfileName);
        Assert.Equal(expectedColor, vm.ProfileBadgeColor);
        Assert.StartsWith(expectedDescPrefix, vm.ProfileDescription);
    }

    [Theory]
    [InlineData(3, "Fast", true, false, false, false)]
    [InlineData(9, "Balanced", false, true, false, false)]
    [InlineData(15, "High", false, false, true, false)]
    [InlineData(22, "Ultra", false, false, false, true)]
    public void SelectPresetCommand_WithInt_SynchronizesSelectionAndSlider(
        int level,
        string expectedActivePreset,
        bool expectFast,
        bool expectBalanced,
        bool expectHigh,
        bool expectUltra)
    {
        var vm = new CompressionSettingsViewModel();

        vm.SelectPresetCommand.Execute(level);

        Assert.Equal(level, vm.CompressionLevel);
        Assert.Equal(expectedActivePreset, vm.ActivePreset);
        Assert.Equal(expectFast, vm.IsFastSelected);
        Assert.Equal(expectBalanced, vm.IsBalancedSelected);
        Assert.Equal(expectHigh, vm.IsHighSelected);
        Assert.Equal(expectUltra, vm.IsUltraSelected);
        Assert.False(vm.IsCustomSelected);
    }

    [Theory]
    [InlineData("Fast", 3)]
    [InlineData("fast", 3)]
    [InlineData("Balanced", 9)]
    [InlineData("High", 15)]
    [InlineData("Ultra", 22)]
    public void SelectPresetCommand_WithString_SetsLevel(string name, int expectedLevel)
    {
        var vm = new CompressionSettingsViewModel();

        vm.SelectPresetCommand.Execute(name);

        Assert.Equal(expectedLevel, vm.CompressionLevel);
        Assert.Equal(name, vm.ActivePreset, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectPresetCommand_WithPresetObject_SetsLevel()
    {
        var vm = new CompressionSettingsViewModel();
        var preset = vm.Presets.First(p => p.Name == "High");

        vm.SelectPresetCommand.Execute(preset);

        Assert.Equal(15, vm.CompressionLevel);
        Assert.Equal("High", vm.ActivePreset);
        Assert.True(vm.IsHighSelected);
    }

    [Fact]
    public void DirectSelectPresetMethods_WorkAsExpected()
    {
        var vm = new CompressionSettingsViewModel();

        vm.SelectPreset(3);
        Assert.Equal(3, vm.CompressionLevel);
        Assert.Equal("Fast", vm.ActivePreset);

        vm.SelectPreset("Ultra");
        Assert.Equal(22, vm.CompressionLevel);
        Assert.Equal("Ultra", vm.ActivePreset);
    }

    [Theory]
    [InlineData(1, null, true, "~50%", "Fastest")]
    [InlineData(5, null, true, "~50%", "Fastest")]
    [InlineData(7, null, true, "~65%", "Balanced")]
    [InlineData(12, null, true, "~75%", "High Ratio")]
    [InlineData(18, null, true, "~75%", "High Ratio")]
    [InlineData(20, null, true, "~80%", "Maximum")]
    public void CustomCompressionLevel_MarksCustomAndComputesEstimates(
        int level,
        string? expectedActive,
        bool expectCustom,
        string expectedRatio,
        string expectedThroughput)
    {
        var vm = new CompressionSettingsViewModel { CompressionLevel = level };

        Assert.Equal(expectedActive, vm.ActivePreset);
        Assert.Equal(expectCustom, vm.IsCustomSelected);
        Assert.Equal(expectedRatio, vm.CurrentRatioEstimate);
        Assert.Equal(expectedThroughput, vm.CurrentThroughputEstimate);
    }

    [Theory]
    [InlineData(1, "Fast", "#28A745")]
    [InlineData(2, "Fast", "#28A745")]
    [InlineData(5, "Fast", "#28A745")]
    [InlineData(6, "Balanced", "#0078D4")]
    [InlineData(9, "Balanced", "#0078D4")]
    [InlineData(11, "Balanced", "#0078D4")]
    [InlineData(12, "High", "#E67E22")]
    [InlineData(15, "High", "#E67E22")]
    [InlineData(18, "High", "#E67E22")]
    [InlineData(19, "Ultra", "#D83B01")]
    [InlineData(22, "Ultra", "#D83B01")]
    public void CompressionLevel_CalculatesProfileNameAndColor(int level, string expectedName, string expectedColor)
    {
        var vm = new CompressionSettingsViewModel { CompressionLevel = level };

        Assert.Equal(expectedName, vm.ProfileName);
        Assert.Equal(expectedColor, vm.ProfileBadgeColor);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(23, 22)]
    [InlineData(100, 22)]
    public void CompressionLevel_ClampsToValidRange(int inputLevel, int expectedLevel)
    {
        var vm = new CompressionSettingsViewModel { CompressionLevel = inputLevel };
        Assert.Equal(expectedLevel, vm.CompressionLevel);
    }

    [Fact]
    public void CompressionLevel_NotifiesDependentProperties()
    {
        var vm = new CompressionSettingsViewModel();
        var changedProps = new List<string>();
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) changedProps.Add(e.PropertyName);
        };

        vm.CompressionLevel = 22;

        Assert.Contains(nameof(CompressionSettingsViewModel.CompressionLevel), changedProps);
        Assert.Contains(nameof(CompressionSettingsViewModel.ProfileName), changedProps);
        Assert.Contains(nameof(CompressionSettingsViewModel.ProfileBadgeColor), changedProps);
        Assert.Contains(nameof(CompressionSettingsViewModel.ProfileDescription), changedProps);
        Assert.Contains(nameof(CompressionSettingsViewModel.ActivePreset), changedProps);
        Assert.Contains(nameof(CompressionSettingsViewModel.IsFastSelected), changedProps);
        Assert.Contains(nameof(CompressionSettingsViewModel.IsBalancedSelected), changedProps);
        Assert.Contains(nameof(CompressionSettingsViewModel.IsHighSelected), changedProps);
        Assert.Contains(nameof(CompressionSettingsViewModel.IsUltraSelected), changedProps);
        Assert.Contains(nameof(CompressionSettingsViewModel.IsCustomSelected), changedProps);
        Assert.Contains(nameof(CompressionSettingsViewModel.CurrentRatioEstimate), changedProps);
        Assert.Contains(nameof(CompressionSettingsViewModel.CurrentThroughputEstimate), changedProps);
    }

    [Fact]
    public void SourcePathChanged_AutoSetsDestinationPath_WhenEmpty()
    {
        var vm = new CompressionSettingsViewModel();
        vm.SourcePath = "/path/to/myfolder";

        Assert.Equal("/path/to/myfolder.zrus", vm.DestinationPath);
    }

    [Fact]
    public void SourcePathChanged_AutoUpdatesDestinationPath_WhenMatchingOldDerivedPath()
    {
        var vm = new CompressionSettingsViewModel();
        vm.SourcePath = "/path/to/first";
        Assert.Equal("/path/to/first.zrus", vm.DestinationPath);

        vm.SourcePath = "/path/to/second";
        Assert.Equal("/path/to/second.zrus", vm.DestinationPath);
    }

    [Fact]
    public void SelectedFormatChanged_UpdatesDestinationPathExtension()
    {
        var vm = new CompressionSettingsViewModel();
        vm.SourcePath = "/path/to/data";
        Assert.Equal("/path/to/data.zrus", vm.DestinationPath);

        vm.SelectedFormat = ".zip";
        Assert.Equal("/path/to/data.zip", vm.DestinationPath);

        vm.SelectedFormat = ".zrus";
        Assert.Equal("/path/to/data.zrus", vm.DestinationPath);
    }

    [Fact]
    public async Task BrowseSourceFileAsync_UpdatesSourcePath_WhenFileSelected()
    {
        var vm = new CompressionSettingsViewModel
        {
            RequestSourceFile = () => Task.FromResult<string?>("/selected/file.txt")
        };

        await vm.BrowseSourceFileCommand.ExecuteAsync(null);

        Assert.Equal("/selected/file.txt", vm.SourcePath);
        Assert.Equal("/selected/file.txt.zrus", vm.DestinationPath);
    }

    [Fact]
    public async Task BrowseSourceFileAsync_PreservesPath_WhenCancelled()
    {
        var vm = new CompressionSettingsViewModel
        {
            SourcePath = "/existing/file.txt",
            RequestSourceFile = () => Task.FromResult<string?>(null)
        };

        await vm.BrowseSourceFileCommand.ExecuteAsync(null);

        Assert.Equal("/existing/file.txt", vm.SourcePath);
    }

    [Fact]
    public async Task BrowseSourceFolderAsync_UpdatesSourcePath_WhenFolderSelected()
    {
        var vm = new CompressionSettingsViewModel
        {
            RequestSourceFolder = () => Task.FromResult<string?>("/selected/folder")
        };

        await vm.BrowseSourceFolderCommand.ExecuteAsync(null);

        Assert.Equal("/selected/folder", vm.SourcePath);
        Assert.Equal("/selected/folder.zrus", vm.DestinationPath);
    }

    [Fact]
    public async Task BrowseSourceFolderAsync_PreservesPath_WhenCancelled()
    {
        var vm = new CompressionSettingsViewModel
        {
            SourcePath = "/existing/folder",
            RequestSourceFolder = () => Task.FromResult<string?>(null)
        };

        await vm.BrowseSourceFolderCommand.ExecuteAsync(null);

        Assert.Equal("/existing/folder", vm.SourcePath);
    }

    [Fact]
    public async Task BrowseDestinationFileAsync_UpdatesDestinationPath_WhenFileSelected()
    {
        var vm = new CompressionSettingsViewModel
        {
            RequestDestinationFile = () => Task.FromResult<string?>("/custom/output.zrus")
        };

        await vm.BrowseDestinationFileCommand.ExecuteAsync(null);

        Assert.Equal("/custom/output.zrus", vm.DestinationPath);
    }

    [Fact]
    public async Task BrowseDestinationFileAsync_PreservesPath_WhenCancelled()
    {
        var vm = new CompressionSettingsViewModel
        {
            DestinationPath = "/existing/output.zrus",
            RequestDestinationFile = () => Task.FromResult<string?>(null)
        };

        await vm.BrowseDestinationFileCommand.ExecuteAsync(null);

        Assert.Equal("/existing/output.zrus", vm.DestinationPath);
    }
}

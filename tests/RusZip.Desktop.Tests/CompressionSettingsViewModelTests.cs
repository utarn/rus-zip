using System.ComponentModel;
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

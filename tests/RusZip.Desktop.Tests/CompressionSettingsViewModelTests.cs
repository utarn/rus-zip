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
        Assert.Equal(2, vm.Formats.Count);
        Assert.Contains(".zrus", vm.Formats);
        Assert.Contains(".zip", vm.Formats);
        Assert.DoesNotContain(".zst", vm.Formats);
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
    public void StageSources_MultiplePaths_SetsCollectionAndPrimarySource()
    {
        var vm = new CompressionSettingsViewModel();

        vm.StageSources(["/tmp/a.txt", "/tmp/b.txt"]);

        Assert.Equal(2, vm.SourcePaths.Count);
        Assert.Equal("/tmp/a.txt", vm.SourcePaths[0]);
        Assert.Equal("/tmp/b.txt", vm.SourcePaths[1]);
        Assert.True(vm.HasMultipleSources);
        Assert.Equal("/tmp/a.txt", vm.SourcePath);
        Assert.Equal("/tmp/Archive.zrus", vm.DestinationPath);
        Assert.Contains("/tmp/a.txt", vm.SourcePathsDisplay);
        Assert.Contains("/tmp/b.txt", vm.SourcePathsDisplay);
    }

    [Fact]
    public void StageSources_SinglePath_BehavesLikeDirectSourceAssignment()
    {
        var vm = new CompressionSettingsViewModel();

        vm.StageSources(["/tmp/only.txt"]);

        Assert.False(vm.HasMultipleSources);
        Assert.Single(vm.SourcePaths);
        Assert.Equal("/tmp/only.txt", vm.SourcePath);
        Assert.Equal("/tmp/only.txt.zrus", vm.DestinationPath);
    }

    [Fact]
    public void StageSources_EditingPrimarySource_ResetsToSingleSource()
    {
        var vm = new CompressionSettingsViewModel();
        vm.StageSources(["/tmp/a.txt", "/tmp/b.txt"]);
        Assert.Equal(2, vm.SourcePaths.Count);

        vm.SourcePath = "/tmp/new.txt";

        Assert.Single(vm.SourcePaths);
        Assert.Equal("/tmp/new.txt", vm.SourcePaths[0]);
        Assert.False(vm.HasMultipleSources);
    }

    [Fact]
    public void SourcePathChanged_NotifiesSourcePathsDisplay()
    {
        var vm = new CompressionSettingsViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) changed.Add(e.PropertyName);
        };

        vm.SourcePath = "/tmp/single.txt";

        Assert.Contains(nameof(CompressionSettingsViewModel.SourcePaths), changed);
        Assert.Contains(nameof(CompressionSettingsViewModel.SourcePathsDisplay), changed);
        Assert.Contains(nameof(CompressionSettingsViewModel.HasMultipleSources), changed);
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
    public void SourcePathChanged_WithNonDefaultSelectedFormat_UsesRegistryLookupForDerivedPath()
    {
        var vm = new CompressionSettingsViewModel();
        vm.SourcePath = "/path/to/first";
        Assert.Equal("/path/to/first.zrus", vm.DestinationPath);

        vm.SelectedFormat = ".zip";
        Assert.Equal("/path/to/first.zip", vm.DestinationPath);

        // Destination was derived from the previous source with a different compressible
        // format; the registry lookup must still detect it and re-derive with the current one.
        vm.SourcePath = "/path/to/second";
        Assert.Equal("/path/to/second.zip", vm.DestinationPath);
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

    [Theory]
    [InlineData(".zst")]
    [InlineData(".tar.zstd")]
    [InlineData(".tzstd")]
    [InlineData(".7z")]
    [InlineData(".rar")]
    [InlineData(".tar.gz")]
    [InlineData(".invalid")]
    public void SelectedFormat_WhenSetToUnsupportedFormat_FallsBackToDefault(string unsupportedFormat)
    {
        var vm = new CompressionSettingsViewModel();
        Assert.Equal(".zrus", vm.SelectedFormat);

        vm.SelectedFormat = unsupportedFormat;

        Assert.Equal(".zrus", vm.SelectedFormat);
    }

    [Fact]
    public void AvailableFormats_StrictlyRestrictedToZrusAndZip()
    {
        var vm = new CompressionSettingsViewModel();

        Assert.Equal(2, CompressionSettingsViewModel.AvailableFormats.Count);
        Assert.Equal([".zrus", ".zip"], CompressionSettingsViewModel.AvailableFormats);
        Assert.Equal(CompressionSettingsViewModel.AvailableFormats, vm.Formats);
    }

    [Fact]
    public async Task AddFilesCommand_WithCallback_StagesMultipleFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "add_files_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var file1 = Path.Combine(tempDir, "file1.txt");
        var file2 = Path.Combine(tempDir, "file2.log");
        File.WriteAllText(file1, "12345");
        File.WriteAllText(file2, "1234567890");

        try
        {
            var vm = new CompressionSettingsViewModel
            {
                RequestSourceFiles = () => Task.FromResult<IReadOnlyList<string>?>([file1, file2])
            };

            await vm.AddFilesCommand.ExecuteAsync(null);

            Assert.Equal(2, vm.StagedItems.Count);
            Assert.Equal(2, vm.TotalFilesCount);
            Assert.Equal(0, vm.ExcludedFilesCount);
            Assert.Equal(15, vm.TotalStagedBytes);
            Assert.Equal("15 B", vm.FormattedTotalStagedBytes);
            Assert.Equal(Path.Combine(tempDir, "Archive.zrus"), vm.DestinationPath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AddFilesCommand_WithParameter_StagesFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "add_files_param_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var file1 = Path.Combine(tempDir, "data.csv");
        File.WriteAllText(file1, "col1,col2");

        try
        {
            var vm = new CompressionSettingsViewModel();
            await vm.AddFilesCommand.ExecuteAsync(new[] { file1 });

            Assert.Single(vm.StagedItems);
            Assert.Equal("data.csv", vm.StagedItems[0].Name);
            Assert.Equal(1, vm.TotalFilesCount);
            Assert.Equal(file1 + ".zrus", vm.DestinationPath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AddFolderCommand_StagesFolderTreeWithHierarchy()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "add_folder_test_" + Guid.NewGuid().ToString("N"));
        var subDir = Path.Combine(tempDir, "nested");
        Directory.CreateDirectory(subDir);
        var file1 = Path.Combine(tempDir, "root_file.txt");
        var file2 = Path.Combine(subDir, "nested_file.txt");
        File.WriteAllText(file1, "1234");
        File.WriteAllText(file2, "123456");

        try
        {
            var vm = new CompressionSettingsViewModel
            {
                RequestSourceFolder = () => Task.FromResult<string?>(tempDir)
            };

            await vm.AddFolderCommand.ExecuteAsync(null);

            Assert.Single(vm.StagedItems);
            Assert.True(vm.StagedItems[0].IsDirectory);
            Assert.Equal(2, vm.TotalFilesCount);
            Assert.Equal(0, vm.ExcludedFilesCount);
            Assert.Equal(10, vm.TotalStagedBytes);
            Assert.Equal(tempDir + ".zrus", vm.DestinationPath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void RemoveSelectedCommand_RootItem_UnstagesItemAndUpdatesMetrics()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "remove_root_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var file1 = Path.Combine(tempDir, "file1.txt");
        var file2 = Path.Combine(tempDir, "file2.txt");
        File.WriteAllText(file1, "12345");
        File.WriteAllText(file2, "12345");

        try
        {
            var vm = new CompressionSettingsViewModel();
            vm.StageSources([file1, file2]);

            Assert.Equal(2, vm.StagedItems.Count);
            Assert.Equal(2, vm.TotalFilesCount);
            Assert.Equal(10, vm.TotalStagedBytes);

            vm.SelectedItem = vm.StagedItems[0];
            vm.RemoveSelectedCommand.Execute(null);

            Assert.Single(vm.StagedItems);
            Assert.Equal(file2, vm.StagedItems[0].FullPath);
            Assert.Null(vm.SelectedItem);
            Assert.Equal(1, vm.TotalFilesCount);
            Assert.Equal(5, vm.TotalStagedBytes);
            Assert.Equal(file2 + ".zrus", vm.DestinationPath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void RemoveSelectedCommand_ChildItem_MarksAsExcludedAndUpdatesExclusionPaths()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "remove_child_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var file1 = Path.Combine(tempDir, "file1.txt");
        var file2 = Path.Combine(tempDir, "file2.txt");
        File.WriteAllText(file1, "12345"); // 5 bytes
        File.WriteAllText(file2, "12345"); // 5 bytes

        try
        {
            var vm = new CompressionSettingsViewModel();
            vm.StageSources([tempDir]);

            Assert.Single(vm.StagedItems);
            var root = vm.StagedItems[0];
            Assert.Equal(2, root.Children.Count);
            Assert.Equal(2, vm.TotalFilesCount);
            Assert.Equal(0, vm.ExcludedFilesCount);
            Assert.Equal(10, vm.TotalStagedBytes);
            Assert.Empty(vm.ExclusionPaths);

            var childToExclude = root.Children[0];
            vm.RemoveSelectedCommand.Execute(childToExclude);

            Assert.True(childToExclude.IsExcluded);
            Assert.Equal(2, vm.TotalFilesCount);
            Assert.Equal(1, vm.ExcludedFilesCount);
            Assert.Equal(5, vm.TotalStagedBytes);
            Assert.Single(vm.ExclusionPaths);
            Assert.Equal(childToExclude.FullPath, vm.ExclusionPaths[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ClearAllCommand_ResetsAllStagingStateAndMetrics()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clear_all_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var file1 = Path.Combine(tempDir, "file1.txt");
        File.WriteAllText(file1, "12345");

        try
        {
            var vm = new CompressionSettingsViewModel();
            vm.StageSources([file1]);

            Assert.Single(vm.StagedItems);
            Assert.Equal(1, vm.TotalFilesCount);
            Assert.Equal(5, vm.TotalStagedBytes);
            Assert.False(string.IsNullOrEmpty(vm.DestinationPath));

            vm.ClearAllCommand.Execute(null);

            Assert.Empty(vm.StagedItems);
            Assert.Empty(vm.ExclusionPaths);
            Assert.Null(vm.SelectedItem);
            Assert.Equal(0, vm.TotalFilesCount);
            Assert.Equal(0, vm.ExcludedFilesCount);
            Assert.Equal(0, vm.TotalStagedBytes);
            Assert.Equal(string.Empty, vm.DestinationPath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ToggleExclusionCommand_TogglesStateAndRecalculatesMetrics()
    {
        var root = new StagedSourceItemViewModel { Name = "dir", IsDirectory = true, FullPath = "/path/dir" };
        var file1 = new StagedSourceItemViewModel { Name = "a.txt", IsDirectory = false, Size = 100, FullPath = "/path/dir/a.txt", Parent = root };
        var file2 = new StagedSourceItemViewModel { Name = "b.txt", IsDirectory = false, Size = 200, FullPath = "/path/dir/b.txt", Parent = root };
        root.Children.Add(file1);
        root.Children.Add(file2);

        var vm = new CompressionSettingsViewModel();
        vm.StagedItems.Add(root);
        vm.RecalculateMetrics();

        Assert.Equal(2, vm.TotalFilesCount);
        Assert.Equal(0, vm.ExcludedFilesCount);
        Assert.Equal(300, vm.TotalStagedBytes);

        // Toggle file1
        vm.ToggleExclusionCommand.Execute(file1);

        Assert.True(file1.IsExcluded);
        Assert.Equal(2, vm.TotalFilesCount);
        Assert.Equal(1, vm.ExcludedFilesCount);
        Assert.Equal(200, vm.TotalStagedBytes);
        Assert.Contains(file1.FullPath, vm.ExclusionPaths);

        // Toggle file1 back
        vm.ToggleExclusionCommand.Execute(file1);

        Assert.False(file1.IsExcluded);
        Assert.Equal(2, vm.TotalFilesCount);
        Assert.Equal(0, vm.ExcludedFilesCount);
        Assert.Equal(300, vm.TotalStagedBytes);
        Assert.DoesNotContain(file1.FullPath, vm.ExclusionPaths);
    }

    [Fact]
    public void SmartDestinationPath_SingleSource_DerivesItemNameFormat()
    {
        var vm = new CompressionSettingsViewModel();
        vm.StageSources(["/home/user/my_folder"]);

        Assert.Equal("/home/user/my_folder.zrus", vm.DestinationPath);
        Assert.False(vm.IsDestinationPinned);
    }

    [Fact]
    public void SmartDestinationPath_MultipleSources_DerivesParentDirArchiveFormat()
    {
        var vm = new CompressionSettingsViewModel();
        vm.StageSources(["/home/user/docs/file1.txt", "/home/user/docs/file2.txt"]);

        Assert.Equal(Path.Combine("/home/user/docs", "Archive.zrus"), vm.DestinationPath);
        Assert.False(vm.IsDestinationPinned);
    }

    [Fact]
    public void SmartDestinationPath_ManualEdit_PinsLockAndPreservesPath()
    {
        var vm = new CompressionSettingsViewModel();
        vm.StageSources(["/home/user/file1.txt"]);
        Assert.Equal("/home/user/file1.txt.zrus", vm.DestinationPath);
        Assert.False(vm.IsDestinationPinned);

        // User manually edits destination
        vm.DestinationPath = "/custom/target/backup.zrus";
        Assert.True(vm.IsDestinationPinned);

        // Adding more sources does not overwrite manual destination
        vm.AddSources(["/home/user/file2.txt"]);
        Assert.Equal("/custom/target/backup.zrus", vm.DestinationPath);
        Assert.True(vm.IsDestinationPinned);
    }

    [Fact]
    public void SmartDestinationPath_ManualEdit_FormatChangeStillUpdatesExtension()
    {
        var vm = new CompressionSettingsViewModel();
        vm.DestinationPath = "/custom/path/archive.zrus";
        Assert.True(vm.IsDestinationPinned);

        vm.SelectedFormat = ".zip";
        Assert.Equal("/custom/path/archive.zip", vm.DestinationPath);
        Assert.True(vm.IsDestinationPinned);
    }

    [Fact]
    public void SmartDestinationPath_ClearingDestinationPath_UnlocksManualLock()
    {
        var vm = new CompressionSettingsViewModel();
        vm.DestinationPath = "/custom/path/archive.zrus";
        Assert.True(vm.IsDestinationPinned);

        vm.DestinationPath = "";
        Assert.False(vm.IsDestinationPinned);

        vm.StageSources(["/home/user/test.txt"]);
        Assert.Equal("/home/user/test.txt.zrus", vm.DestinationPath);
    }

    [Fact]
    public void ExpandAllAndCollapseAll_TogglesHierarchyRecursively()
    {
        var root = new StagedSourceItemViewModel { Name = "root", IsDirectory = true };
        var child = new StagedSourceItemViewModel { Name = "child", IsDirectory = true, Parent = root };
        root.Children.Add(child);

        var vm = new CompressionSettingsViewModel();
        vm.StagedItems.Add(root);

        Assert.False(root.IsExpanded);
        Assert.False(child.IsExpanded);

        vm.ExpandAllCommand.Execute(null);
        Assert.True(root.IsExpanded);
        Assert.True(child.IsExpanded);

        vm.CollapseAllCommand.Execute(null);
        Assert.False(root.IsExpanded);
        Assert.False(child.IsExpanded);
    }
}


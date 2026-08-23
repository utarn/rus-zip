using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
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
    [Fact]
    public void CreateSourceFilesPickerOptions_SetsAllowMultipleTrue()
    {
        var options = CompressionSettingsView.CreateSourceFilesPickerOptions();

        Assert.Equal("Select File(s) to Compress", options.Title);
        Assert.True(options.AllowMultiple);
    }

    [Fact]
    public void CreateSourceFolderPickerOptions_SetsAllowMultipleFalse()
    {
        var options = CompressionSettingsView.CreateSourceFolderPickerOptions();

        Assert.Equal("Select Folder to Compress", options.Title);
        Assert.False(options.AllowMultiple);
    }

    [Fact]
    public void CreateSingleSourceFilePickerOptions_SetsAllowMultipleFalse()
    {
        var options = CompressionSettingsView.CreateSingleSourceFilePickerOptions();

        Assert.Equal("Select File to Compress", options.Title);
        Assert.False(options.AllowMultiple);
    }

    [AvaloniaFact]
    public void StagedGrid_Columns_And_SelectionMode_ConfiguredCorrectly()
    {
        var vm = new CompressionSettingsViewModel();
        var view = new CompressionSettingsView { DataContext = vm };

        var stagedGrid = view.FindControl<DataGrid>("StagedGrid");
        Assert.NotNull(stagedGrid);
        Assert.Equal(DataGridSelectionMode.Extended, stagedGrid.SelectionMode);
        Assert.True(stagedGrid.HierarchicalRowsEnabled);

        Assert.Equal(5, stagedGrid.Columns.Count);
        Assert.Equal("Name", stagedGrid.Columns[0].Header?.ToString());
        Assert.IsType<DataGridHierarchicalColumn>(stagedGrid.Columns[0]);

        Assert.Equal("Size", stagedGrid.Columns[1].Header?.ToString());
        Assert.Equal("Modified", stagedGrid.Columns[2].Header?.ToString());
        Assert.Equal("Attributes", stagedGrid.Columns[3].Header?.ToString());
        Assert.Equal("Path", stagedGrid.Columns[4].Header?.ToString());
    }

    [AvaloniaFact]
    public void StagingToolbar_Buttons_And_Commands_Configured()
    {
        var vm = new CompressionSettingsViewModel();
        var view = new CompressionSettingsView { DataContext = vm };

        var buttons = view.GetLogicalDescendants()
            .OfType<Button>()
            .ToList();

        var addFilesBtn = buttons.FirstOrDefault(b => b.Command == vm.AddFilesCommand);
        Assert.NotNull(addFilesBtn);

        var addFolderBtn = buttons.FirstOrDefault(b => b.Command == vm.AddFolderCommand);
        Assert.NotNull(addFolderBtn);

        var removeBtn = buttons.FirstOrDefault(b => b.Command == vm.RemoveSelectedCommand);
        Assert.NotNull(removeBtn);

        var clearBtn = buttons.FirstOrDefault(b => b.Command == vm.ClearAllCommand);
        Assert.NotNull(clearBtn);
    }

    [AvaloniaFact]
    public void KeyboardShortcut_Delete_RemovesSelectedItem()
    {
        var vm = new CompressionSettingsViewModel();
        var tempDir = Path.Combine(Path.GetTempPath(), "test_delete_key_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var file1 = Path.Combine(tempDir, "file1.txt");
        var file2 = Path.Combine(tempDir, "file2.txt");
        File.WriteAllText(file1, "123");
        File.WriteAllText(file2, "456");

        try
        {
            vm.StageSources([file1, file2]);
            var view = new CompressionSettingsView { DataContext = vm };
            var stagedGrid = view.FindControl<DataGrid>("StagedGrid");
            Assert.NotNull(stagedGrid);

            vm.SelectedItem = vm.StagedItems[0];
            Assert.Equal(2, vm.StagedItems.Count);

            var keyEventArgs = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Delete
            };
            stagedGrid.RaiseEvent(keyEventArgs);

            Assert.True(keyEventArgs.Handled);
            Assert.Single(vm.StagedItems);
            Assert.Equal(file2, vm.StagedItems[0].FullPath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [AvaloniaFact]
    public void KeyboardShortcut_Backspace_RemovesSelectedItem()
    {
        var vm = new CompressionSettingsViewModel();
        var tempDir = Path.Combine(Path.GetTempPath(), "test_back_key_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var file1 = Path.Combine(tempDir, "file1.txt");
        File.WriteAllText(file1, "123");

        try
        {
            vm.StageSources([file1]);
            var view = new CompressionSettingsView { DataContext = vm };
            var stagedGrid = view.FindControl<DataGrid>("StagedGrid");
            Assert.NotNull(stagedGrid);

            vm.SelectedItem = vm.StagedItems[0];
            Assert.Single(vm.StagedItems);

            var keyEventArgs = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Back
            };
            stagedGrid.RaiseEvent(keyEventArgs);

            Assert.True(keyEventArgs.Handled);
            Assert.Empty(vm.StagedItems);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [AvaloniaFact]
    public void KeyboardShortcut_Delete_WithMultipleSelectedItems_RemovesAllSelected()
    {
        var vm = new CompressionSettingsViewModel();
        var tempDir = Path.Combine(Path.GetTempPath(), "test_multi_delete_key_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var file1 = Path.Combine(tempDir, "file1.txt");
        var file2 = Path.Combine(tempDir, "file2.txt");
        var file3 = Path.Combine(tempDir, "file3.txt");
        File.WriteAllText(file1, "123");
        File.WriteAllText(file2, "456");
        File.WriteAllText(file3, "789");

        try
        {
            vm.StageSources([file1, file2, file3]);
            var view = new CompressionSettingsView { DataContext = vm };
            var stagedGrid = view.FindControl<DataGrid>("StagedGrid");
            Assert.NotNull(stagedGrid);

            stagedGrid.SelectedItems.Add(vm.StagedItems[0]);
            stagedGrid.SelectedItems.Add(vm.StagedItems[1]);

            var keyEventArgs = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Delete
            };
            stagedGrid.RaiseEvent(keyEventArgs);

            Assert.True(keyEventArgs.Handled);
            Assert.Single(vm.StagedItems);
            Assert.Equal(file3, vm.StagedItems[0].FullPath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MainWindowAxaml_CreateArchiveDialog_HasResponsiveDimensions()
    {
        var desktopPath = FindDesktopProjectPath();
        var mainWindowAxamlFile = Path.Combine(desktopPath, "Views", "MainWindow.axaml");
        Assert.True(File.Exists(mainWindowAxamlFile));

        var doc = XDocument.Load(mainWindowAxamlFile);
        var root = doc.Root;
        Assert.NotNull(root);

        var compressOverlay = root.Descendants()
            .FirstOrDefault(e => e.Attributes().Any(a => a.Name.LocalName == "IsVisible" && a.Value.Contains("IsCompressDialogVisible")));
        Assert.NotNull(compressOverlay);

        var dialogBorder = compressOverlay.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "Border");
        Assert.NotNull(dialogBorder);

        Assert.Equal("780", dialogBorder.Attribute("Width")?.Value);
        Assert.Equal("580", dialogBorder.Attribute("Height")?.Value);
        Assert.Equal("680", dialogBorder.Attribute("MinWidth")?.Value);
        Assert.Equal("480", dialogBorder.Attribute("MinHeight")?.Value);
    }

    [Fact]
    public void CompressionSettingsViewAxaml_HasExclusionStylesAndColumns()
    {
        var desktopPath = FindDesktopProjectPath();
        var axamlFile = Path.Combine(desktopPath, "Views", "CompressionSettingsView.axaml");
        Assert.True(File.Exists(axamlFile));

        var doc = XDocument.Load(axamlFile);
        var root = doc.Root;
        Assert.NotNull(root);

        var styles = root.Descendants()
            .Where(e => e.Name.LocalName == "Style")
            .ToList();
        Assert.NotEmpty(styles);

        var excludedTextblockStyle = styles.FirstOrDefault(s => s.Attribute("Selector")?.Value.Contains("TextBlock.excluded") == true);
        Assert.NotNull(excludedTextblockStyle);

        var strikethroughSetter = excludedTextblockStyle.Elements()
            .FirstOrDefault(e => e.Attribute("Property")?.Value == "TextDecorations" && e.Attribute("Value")?.Value == "Strikethrough");
        Assert.NotNull(strikethroughSetter);
    }

    private static string FindDesktopProjectPath()
    {
        var currentDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(currentDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RusZip.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            throw new DirectoryNotFoundException("Could not find repository root containing RusZip.slnx");
        }

        return Path.Combine(dir.FullName, "src", "RusZip.Desktop");
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using RusZip.Desktop.Models;
using RusZip.Desktop.Services;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public class MainWindowMenuAndShortcutsTests
{
    [AvaloniaFact]
    public void MainWindow_NativeMenu_ConfiguredWithAllStandardMenus()
    {
        var window = new MainWindow();
        var nativeMenu = NativeMenu.GetMenu(window);

        Assert.NotNull(nativeMenu);
        Assert.Equal(6, nativeMenu.Items.Count);

        var fileItem = Assert.IsAssignableFrom<NativeMenuItem>(nativeMenu.Items[0]);
        var editItem = Assert.IsAssignableFrom<NativeMenuItem>(nativeMenu.Items[1]);
        var viewItem = Assert.IsAssignableFrom<NativeMenuItem>(nativeMenu.Items[2]);
        var archiveItem = Assert.IsAssignableFrom<NativeMenuItem>(nativeMenu.Items[3]);
        var toolsItem = Assert.IsAssignableFrom<NativeMenuItem>(nativeMenu.Items[4]);
        var helpItem = Assert.IsAssignableFrom<NativeMenuItem>(nativeMenu.Items[5]);

        Assert.Equal("File", fileItem.Header);
        Assert.Equal("Edit", editItem.Header);
        Assert.Equal("View", viewItem.Header);
        Assert.Equal("Archive", archiveItem.Header);
        Assert.Equal("Tools", toolsItem.Header);
        Assert.Equal("Help", helpItem.Header);

        Assert.NotNull(fileItem.Menu);
        Assert.NotNull(editItem.Menu);
        Assert.NotNull(viewItem.Menu);
        Assert.NotNull(archiveItem.Menu);
        Assert.NotNull(toolsItem.Menu);
        Assert.NotNull(helpItem.Menu);
    }

    [AvaloniaFact]
    public void MainWindow_InWindowMenu_ConfiguredWithExpectedHeadersAndGestures()
    {
        var window = new MainWindow();
        var menu = window.FindControl<Menu>("AppMenuBar");

        Assert.NotNull(menu);
        Assert.Equal(6, menu.Items.Count);

        var topHeaders = menu.Items.OfType<MenuItem>().Select(m => m.Header?.ToString()).ToList();
        Assert.Contains(topHeaders, h => h != null && h.Contains("File"));
        Assert.Contains(topHeaders, h => h != null && h.Contains("Edit"));
        Assert.Contains(topHeaders, h => h != null && h.Contains("View"));
        Assert.Contains(topHeaders, h => h != null && h.Contains("Archive"));
        Assert.Contains(topHeaders, h => h != null && h.Contains("Tools"));
        Assert.Contains(topHeaders, h => h != null && h.Contains("Help"));
    }

    [AvaloniaFact]
    public void MainWindow_KeyBindings_ContainsAllRequiredAccelerators()
    {
        var window = new MainWindow();
        var bindings = window.KeyBindings;

        Assert.NotNull(bindings);
        Assert.NotEmpty(bindings);

        var gestureStrings = bindings.Select(b => b.Gesture?.ToString()).Where(s => s != null).ToList();

        Assert.Contains(gestureStrings, g => g != null && (g.Contains("Ctrl+N") || g.Contains("Control+N") || g.Contains("Cmd+N")));
        Assert.Contains(gestureStrings, g => g != null && (g.Contains("Ctrl+O") || g.Contains("Control+O") || g.Contains("Cmd+O")));
        Assert.Contains(gestureStrings, g => g != null && (g.Contains("Ctrl+W") || g.Contains("Control+W") || g.Contains("Cmd+W")));
        Assert.Contains(gestureStrings, g => g != null && g.Contains("Alt+F4"));
        Assert.Contains(gestureStrings, g => g != null && (g.Contains("Ctrl+A") || g.Contains("Control+A") || g.Contains("Cmd+A")));
        Assert.Contains(gestureStrings, g => g != null && (g.Contains("Ctrl+C") || g.Contains("Control+C") || g.Contains("Cmd+C")));
        Assert.Contains(gestureStrings, g => g != null && (g.Contains("Delete") || g.Contains("Del")));
        Assert.Contains(gestureStrings, g => g != null && g.Contains("F5"));
        Assert.Contains(gestureStrings, g => g != null && (g.Contains("Ctrl+Shift+E") || g.Contains("Control+Shift+E")));
        Assert.Contains(gestureStrings, g => g != null && (g.Contains("Ctrl+F") || g.Contains("Control+F")));
        Assert.Contains(gestureStrings, g => g != null && (g.Contains("Ctrl+Shift+A") || g.Contains("Control+Shift+A")));
        Assert.Contains(gestureStrings, g => g != null && (g.Contains("Ctrl+E") || g.Contains("Control+E")));
        Assert.Contains(gestureStrings, g => g != null && (g.Contains("Ctrl+T") || g.Contains("Control+T")));
        Assert.Contains(gestureStrings, g => g != null && (g.Contains("Alt+Enter") || g.Contains("Alt+Return")));
        Assert.Contains(gestureStrings, g => g != null && (g.Contains("Ctrl+OemComma") || g.Contains("Ctrl+,") || g.Contains("Control+,")));
        Assert.Contains(gestureStrings, g => g != null && g.Contains("F1"));
    }

    [AvaloniaFact]
    public void MainWindowViewModel_SelectAllAndInvertSelection_ModifiesBrowserSelection()
    {
        var engine = new UnifiedArchiveEngine();
        var vm = new MainWindowViewModel(engine);

        var entries = new List<ArchiveEntry>
        {
            new("doc1.txt", 100, 50, DateTimeOffset.UtcNow, false),
            new("doc2.txt", 200, 80, DateTimeOffset.UtcNow, false),
            new("sub/doc3.txt", 300, 120, DateTimeOffset.UtcNow, false),
        };

        vm.Browser.LoadEntries("test.zip", entries);
        vm.HasOpenArchive = true;

        Assert.Empty(vm.Browser.SelectedItems);

        // Select All
        vm.SelectAllCommand.Execute(null);
        Assert.True(vm.Browser.SelectedItems.Count >= 3);

        // Select single item
        var firstItem = vm.Browser.RootItems[0];
        vm.Browser.SetSelectedItems([firstItem]);
        Assert.Single(vm.Browser.SelectedItems);

        // Invert Selection
        vm.InvertSelectionCommand.Execute(null);
        Assert.DoesNotContain(firstItem, vm.Browser.SelectedItems);
        Assert.True(vm.Browser.SelectedItems.Count > 0);
    }

    [AvaloniaFact]
    public void MainWindowViewModel_MenuCommands_ExecuteCorrectly()
    {
        var engine = new UnifiedArchiveEngine();
        var vm = new MainWindowViewModel(engine);

        // Test dialog opening commands
        Assert.False(vm.IsCompressDialogVisible);
        vm.CreateArchiveCommand.Execute(null);
        Assert.True(vm.IsCompressDialogVisible);
        vm.CloseCompressDialogCommand.Execute(null);
        Assert.False(vm.IsCompressDialogVisible);

        // Test settings command
        Assert.False(vm.IsSettingsDialogVisible);
        vm.OpenSettingsCommand.Execute(null);
        Assert.True(vm.IsSettingsDialogVisible);
        vm.CloseSettingsDialogCommand.Execute(null);
        Assert.False(vm.IsSettingsDialogVisible);

        // Test theme switcher
        var initialTheme = vm.CurrentTheme;
        vm.ToggleThemeCommand.Execute(null);
        Assert.NotEqual(initialTheme, vm.CurrentTheme);

        // Test informational menu commands
        vm.OpenDocumentationCommand.Execute(null);
        Assert.Contains("Documentation", vm.StatusText);

        vm.OpenSupportedFormatsCommand.Execute(null);
        Assert.Contains("Supported formats", vm.StatusText);

        vm.ShowAboutCommand.Execute(null);
        Assert.Contains("rus-zip", vm.StatusText);

        // Test archive state commands
        vm.HasOpenArchive = true;
        vm.TestArchiveCommand.Execute(null);
        Assert.Contains("Testing archive integrity", vm.StatusText);

        vm.ShowPropertiesCommand.Execute(null);
        Assert.Contains("properties inspector", vm.StatusText);

        bool exitRequested = false;
        vm.RequestExit = () => exitRequested = true;
        vm.ExitApplicationCommand.Execute(null);
        Assert.True(exitRequested);

        bool focusFilterRequested = false;
        vm.RequestFocusFilter = () => focusFilterRequested = true;
        vm.FocusFilterCommand.Execute(null);
        Assert.True(focusFilterRequested);
    }

    [AvaloniaFact]
    public void MainWindowViewModel_ExpandAndCollapseAll_PropagatesToBrowser()
    {
        var engine = new UnifiedArchiveEngine();
        var vm = new MainWindowViewModel(engine);

        var entries = new List<ArchiveEntry>
        {
            new("dir1/file1.txt", 100, 50, DateTimeOffset.UtcNow, false),
            new("dir1/dir2/file2.txt", 200, 80, DateTimeOffset.UtcNow, false),
        };

        vm.Browser.LoadEntries("test.zip", entries);
        vm.HasOpenArchive = true;

        vm.ExpandAllCommand.Execute(null);
        var dir1 = vm.Browser.RootItems.FirstOrDefault(i => i.Name == "dir1");
        Assert.NotNull(dir1);
        Assert.True(dir1.IsExpanded);

        vm.CollapseAllCommand.Execute(null);
        Assert.False(dir1.IsExpanded);
    }
}

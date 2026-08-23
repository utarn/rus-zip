using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RusZip.Core.Engines;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public class MainWindowViewInteractionTests
{
    [AvaloniaFact]
    public void MainWindow_OpenArchivePickerOptions_AreConfiguredCorrectly()
    {
        var options = MainWindow.CreateOpenArchivePickerOptions();

        Assert.Equal("Open Archive", options.Title);
        Assert.False(options.AllowMultiple);
        Assert.NotNull(options.FileTypeFilter);
        Assert.Equal(8, options.FileTypeFilter.Count);
    }

    [AvaloniaFact]
    public void MainWindow_OpenArchivePickerFileTypes_ContainsExpectedFilters()
    {
        var types = MainWindow.OpenArchivePickerFileTypes;
        Assert.Equal(8, types.Count);
        Assert.Contains(types, t => t.Name.StartsWith("Supported Archives"));
        Assert.Contains(types, t => t.Name.StartsWith("Zstandard Tar Archives"));
        Assert.Contains(types, t => t.Name.StartsWith("Zstandard Compressed Files"));
        Assert.Contains(types, t => t.Name.StartsWith("Zip Archives"));
        Assert.Contains(types, t => t.Name.StartsWith("7-Zip Archives"));
        Assert.Contains(types, t => t.Name.StartsWith("RAR Archives"));
        Assert.Contains(types, t => t.Name.StartsWith("GZip Archives"));
        Assert.Contains(types, t => t.Name.StartsWith("All Files"));
    }

    [AvaloniaFact]
    public void MainWindow_DataContextChanged_WiresViewModelCallbacks()
    {
        var engine = new UnifiedArchiveEngine();
        var vm = new MainWindowViewModel(engine);
        var window = new MainWindow
        {
            DataContext = vm
        };

        Assert.NotNull(vm.RequestExtractDestinationFolder);
        Assert.NotNull(vm.RequestOpenArchivePicker);
        Assert.NotNull(vm.RequestAppendSourcePaths);
        Assert.NotNull(vm.ConfirmDeleteAsync);
    }
}

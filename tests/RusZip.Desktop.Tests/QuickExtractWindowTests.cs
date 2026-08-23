using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using RusZip.Core.Abstractions;
using RusZip.Core.Engines;
using RusZip.Desktop.Models;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public class QuickExtractWindowTests
{
    [AvaloniaFact]
    public void QuickExtractWindow_DefaultConstructor_Initializes()
    {
        var window = new QuickExtractWindow();
        Assert.NotNull(window);
    }

    [AvaloniaFact]
    public void QuickExtractWindow_DataContextChanged_ConfiguresCallbacks()
    {
        var options = new QuickExtractOptions(QuickExtractMode.ExtractHere, "/tmp/test.zip", "/tmp/dest");
        var engine = new UnifiedArchiveEngine();
        var vm = new QuickExtractViewModel(engine, options);

        var window = new QuickExtractWindow
        {
            DataContext = vm
        };

        Assert.NotNull(vm.RequestClose);
        Assert.NotNull(vm.RequestFolderPicker);
        Assert.NotNull(vm.RequestConflictResolution);

        // Test RequestClose callback
        vm.RequestClose();
    }

    [AvaloniaFact]
    public void QuickExtractWindow_UserInteraction_CancelsAutoCloseCountdown()
    {
        var options = new QuickExtractOptions(QuickExtractMode.ExtractHere, "/tmp/test.zip", "/tmp/dest");
        var engine = new UnifiedArchiveEngine();
        var vm = new QuickExtractViewModel(engine, options);

        var window = new QuickExtractWindow
        {
            DataContext = vm
        };

        // Start auto close countdown
        vm.StartAutoCloseCountdown();
        Assert.True(vm.IsAutoCloseActive);

        // Raise KeyDown event
        var keyEventArgs = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Space
        };
        window.RaiseEvent(keyEventArgs);

        Assert.False(vm.IsAutoCloseActive);
    }
}

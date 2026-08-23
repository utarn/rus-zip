using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RusZip.Core.Abstractions;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Views;

public partial class QuickExtractWindow : Window
{
    public QuickExtractWindow()
    {
        InitializeComponent();

        AddHandler(PointerMovedEvent, OnUserInteraction, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnUserInteraction, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnUserInteraction, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        Loaded += OnLoadedAsync;
    }

    private async void OnLoadedAsync(object? sender, RoutedEventArgs e)
    {
        if (DataContext is QuickExtractViewModel vm && !vm.HasStarted)
        {
            await vm.StartExtractionAsync();
        }
    }

    private void OnUserInteraction(object? sender, RoutedEventArgs e)
    {
        if (DataContext is QuickExtractViewModel vm && vm.IsAutoCloseActive)
        {
            vm.CancelAutoCloseCountdown();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is QuickExtractViewModel vm)
        {
            vm.RequestClose = () => Close();

            vm.RequestFolderPicker = async () =>
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Destination Directory for Extraction",
                    AllowMultiple = false
                });

                if (folders.Count > 0)
                {
                    return folders[0].TryGetLocalPath();
                }
                return null;
            };

            vm.RequestConflictResolution = async (context) =>
            {
                var conflictVm = new FileConflictViewModel(context);
                var dialog = new FileConflictDialog(conflictVm);
                return await dialog.ShowDialog<FileConflictResolution>(this);
            };
        }
    }
}

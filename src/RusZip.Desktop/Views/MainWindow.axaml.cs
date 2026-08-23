using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Views;

public partial class MainWindow : Window
{
    public const double MacOsTrafficLightMargin = 76.0;

    public MainWindow()
    {
        InitializeComponent();

        ApplyPlatformWindowChrome();
        Loaded += (_, _) => ApplyPlatformWindowChrome();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public void ApplyPlatformWindowChrome(bool? isMacOSOverride = null)
    {
        var isMacOS = isMacOSOverride ?? OperatingSystem.IsMacOS();
        var contentGrid = this.FindControl<Grid>("TitleBarContentGrid");
        if (contentGrid != null)
        {
            contentGrid.Margin = GetPlatformTitleBarMargin(isMacOS);
        }
    }

    public static Thickness GetPlatformTitleBarMargin(bool isMacOS)
    {
        return isMacOS ? new Thickness(MacOsTrafficLightMargin, 0, 0, 0) : new Thickness(0);
    }

    public void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
            {
                if (CanResize)
                {
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                }
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }

    public static IReadOnlyList<FilePickerFileType> OpenArchivePickerFileTypes =>
    [
        new FilePickerFileType("Supported Archives (*.zrus, *.zip, *.rar, *.7z, *.gz, *.tar.gz, *.tgz, *.tar.zstd, *.tzstd, *.zst)")
        {
            Patterns = ["*.zrus", "*.zip", "*.rar", "*.7z", "*.gz", "*.tar.gz", "*.tgz", "*.tar", "*.tar.zstd", "*.tzstd", "*.zst"]
        },
        new FilePickerFileType("Zstandard Tar Archives (*.zrus, *.tar.zstd, *.tzstd)") { Patterns = ["*.zrus", "*.tar.zstd", "*.tzstd"] },
        new FilePickerFileType("Zstandard Compressed Files (*.zst)") { Patterns = ["*.zst"] },
        new FilePickerFileType("Zip Archives (*.zip)") { Patterns = ["*.zip"] },
        new FilePickerFileType("7-Zip Archives (*.7z)") { Patterns = ["*.7z"] },
        new FilePickerFileType("RAR Archives (*.rar)") { Patterns = ["*.rar"] },
        new FilePickerFileType("GZip Archives (*.gz, *.tar.gz, *.tgz)") { Patterns = ["*.gz", "*.tar.gz", "*.tgz"] },
        new FilePickerFileType("All Files (*.*)") { Patterns = ["*.*"] }
    ];

    public static FilePickerOpenOptions CreateOpenArchivePickerOptions() => new()
    {
        Title = "Open Archive",
        AllowMultiple = false,
        FileTypeFilter = OpenArchivePickerFileTypes.ToList()
    };

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.RequestExtractDestinationFolder = async () =>
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

            vm.RequestOpenArchivePicker = async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(CreateOpenArchivePickerOptions());

                if (files.Count > 0)
                {
                    return files[0].TryGetLocalPath();
                }
                return null;
            };

            vm.RequestAppendSourcePaths = async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select File(s) to Add to Archive",
                    AllowMultiple = true
                });

                if (files.Count > 0)
                {
                    return files.Select(f => f.TryGetLocalPath())
                                .Where(p => !string.IsNullOrEmpty(p))
                                .Select(p => p!)
                                .ToList();
                }
                return null;
            };

            vm.ConfirmDeleteAsync = async (count, paths) =>
            {
                var archiveName = !string.IsNullOrEmpty(vm.Browser.LoadedArchivePath)
                    ? Path.GetFileName(vm.Browser.LoadedArchivePath)
                    : string.Empty;
                var dialogVm = new DeleteConfirmationViewModel(count, paths, archiveName);
                var dialog = new DeleteConfirmationDialog(dialogVm);
                var result = await dialog.ShowDialog<bool>(this);
                return result;
            };
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && e.DataTransfer.Contains(DataFormat.File))
        {
            vm.IsDragOver = true;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsDragOver = false;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            if (DataContext is MainWindowViewModel vm)
            {
                vm.IsDragOver = true;
            }
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
            if (DataContext is MainWindowViewModel vm)
            {
                vm.IsDragOver = false;
            }
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.IsDragOver = false;

        var items = e.DataTransfer.TryGetFiles();
        if (items == null) return;

        var paths = new List<string>();
        foreach (var item in items)
        {
            var localPath = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath))
            {
                paths.Add(localPath);
            }
        }

        if (paths.Count > 0)
        {
            await vm.HandleDroppedPathsAsync(paths);
        }
    }

    private async void OnOpenArchiveClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(CreateOpenArchivePickerOptions());

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                await vm.OpenArchiveAsync(path);
            }
        }
    }

    private async void OnExtractAllClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Destination Directory for Extraction",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                await vm.ExecuteExtractAllAsync(path);
            }
        }
    }
}

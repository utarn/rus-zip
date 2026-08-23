using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Views;

public partial class CompressionSettingsView : UserControl
{
    public CompressionSettingsView()
    {
        InitializeComponent();
        StagedGrid.KeyDown += OnStagedGridKeyDown;
    }

    private void OnStagedGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back && DataContext is CompressionSettingsViewModel vm)
        {
            if (vm.SelectedItem != null)
            {
                vm.RemoveSelectedCommand.Execute(vm.SelectedItem);
                e.Handled = true;
            }
        }
    }

    public static FilePickerSaveOptions CreateSaveFilePickerOptions(CompressionSettingsViewModel vm)
    {
        var isZip = string.Equals(vm.SelectedFormat, ".zip", StringComparison.OrdinalIgnoreCase);
        var ext = isZip ? "zip" : "zrus";
        var pattern = isZip ? "*.zip" : "*.zrus";
        var typeName = isZip ? "ZIP Archive (*.zip)" : "ZRUS Archive (*.zrus)";

        string suggestedFileName;
        if (!string.IsNullOrEmpty(vm.DestinationPath))
        {
            suggestedFileName = Path.GetFileName(vm.DestinationPath);
        }
        else if (vm.StagedItems.Count > 1)
        {
            suggestedFileName = $"Archive{vm.SelectedFormat}";
        }
        else if (vm.StagedItems.Count == 1)
        {
            var single = vm.StagedItems[0].FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            suggestedFileName = Path.GetFileName(single) + vm.SelectedFormat;
        }
        else if (!string.IsNullOrEmpty(vm.SourcePath))
        {
            var single = vm.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            suggestedFileName = Path.GetFileName(single) + vm.SelectedFormat;
        }
        else
        {
            suggestedFileName = $"archive{vm.SelectedFormat}";
        }

        return new FilePickerSaveOptions
        {
            Title = "Save Archive As",
            DefaultExtension = ext,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices =
            [
                new FilePickerFileType(typeName) { Patterns = [pattern] },
                new FilePickerFileType("All Files (*.*)") { Patterns = ["*.*"] }
            ]
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is CompressionSettingsViewModel vm)
        {
            vm.RequestSourceFiles = async () =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return null;
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select File(s) to Compress",
                    AllowMultiple = true
                });
                return files.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
            };

            vm.RequestSourceFile = async () =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return null;
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select File to Compress",
                    AllowMultiple = false
                });
                return files.Count > 0 ? files[0].TryGetLocalPath() : null;
            };

            vm.RequestSourceFolder = async () =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return null;
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Folder to Compress",
                    AllowMultiple = false
                });
                return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            };

            vm.RequestDestinationFile = async () =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return null;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(CreateSaveFilePickerOptions(vm));
                return file?.TryGetLocalPath();
            };
        }
    }
}

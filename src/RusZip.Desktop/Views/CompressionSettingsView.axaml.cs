using Avalonia.Controls;
using Avalonia.Platform.Storage;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Views;

public partial class CompressionSettingsView : UserControl
{
    public CompressionSettingsView()
    {
        InitializeComponent();
    }

    public static FilePickerSaveOptions CreateSaveFilePickerOptions(CompressionSettingsViewModel vm)
    {
        var isZip = string.Equals(vm.SelectedFormat, ".zip", StringComparison.OrdinalIgnoreCase);
        var ext = isZip ? "zip" : "zrus";
        var pattern = isZip ? "*.zip" : "*.zrus";
        var typeName = isZip ? "ZIP Archive (*.zip)" : "ZRUS Archive (*.zrus)";

        return new FilePickerSaveOptions
        {
            Title = "Save Archive As",
            DefaultExtension = ext,
            SuggestedFileName = !string.IsNullOrEmpty(vm.SourcePath)
                ? Path.GetFileName(vm.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + vm.SelectedFormat
                : $"archive{vm.SelectedFormat}",
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

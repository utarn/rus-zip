using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RusZip.Desktop.ViewModels;

public sealed partial class RecentArchiveItemViewModel : ObservableObject
{
    public string FullPath { get; }
    public string FileName { get; }
    public string DirectoryName { get; }
    public string ExtensionBadge { get; }

    public IAsyncRelayCommand<string> OpenCommand { get; }
    public IAsyncRelayCommand<string> RemoveCommand { get; }

    public RecentArchiveItemViewModel(
        string fullPath,
        IAsyncRelayCommand<string> openCommand,
        IAsyncRelayCommand<string> removeCommand)
    {
        FullPath = fullPath;
        FileName = Path.GetFileName(fullPath);
        DirectoryName = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var ext = Path.GetExtension(fullPath);
        ExtensionBadge = string.IsNullOrEmpty(ext) ? "ARCHIVE" : ext.ToUpperInvariant().TrimStart('.');
        OpenCommand = openCommand;
        RemoveCommand = removeCommand;
    }
}

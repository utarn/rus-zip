using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RusZip.Desktop.ViewModels;

public partial class DeleteConfirmationViewModel : ObservableObject
{
    [ObservableProperty] private int _entryCount;
    [ObservableProperty] private string _archiveName = string.Empty;
    [ObservableProperty] private IReadOnlyList<string> _entryPaths = [];
    [ObservableProperty] private string _message = string.Empty;

    public Action<bool>? CloseWithResult { get; set; }

    public DeleteConfirmationViewModel() : this(0, [], string.Empty)
    {
    }

    public DeleteConfirmationViewModel(int entryCount, IReadOnlyList<string> entryPaths, string archiveName = "")
    {
        _entryCount = entryCount;
        _entryPaths = entryPaths;
        _archiveName = archiveName;
        _message = entryCount switch
        {
            1 when entryPaths.Count > 0 => $"Are you sure you want to permanently delete '{entryPaths[0]}' from the archive?",
            _ => $"Are you sure you want to permanently delete {entryCount:N0} selected items from the archive?"
        };
    }

    [RelayCommand]
    public void Confirm()
    {
        CloseWithResult?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel()
    {
        CloseWithResult?.Invoke(false);
    }
}

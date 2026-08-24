using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RusZip.Desktop.ViewModels;

public partial class PasswordPromptViewModel : ObservableObject
{
    [ObservableProperty] private string _archiveName = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isPasswordVisible;

    public Action<string?>? CloseWithResult { get; set; }

    public PasswordPromptViewModel() : this(string.Empty)
    {
    }

    public PasswordPromptViewModel(string archiveName)
    {
        _archiveName = archiveName;
    }

    [RelayCommand]
    public void Submit()
    {
        if (string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Password cannot be empty.";
            return;
        }

        CloseWithResult?.Invoke(Password);
    }

    [RelayCommand]
    public void Cancel()
    {
        CloseWithResult?.Invoke(null);
    }

    [RelayCommand]
    public void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
    }
}

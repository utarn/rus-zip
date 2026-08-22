using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RusZip.Desktop.ViewModels;

public partial class BreadcrumbItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FontWeight))]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FontWeight))]
    private bool _isLast;

    [ObservableProperty]
    private bool _isRoot;

    public IRelayCommand? NavigateCommand { get; set; }

    public FontWeight FontWeight => IsLast ? FontWeight.SemiBold : FontWeight.Normal;

    public BreadcrumbItemViewModel()
    {
    }

    public BreadcrumbItemViewModel(
        string name,
        string fullPath,
        bool isLast = false,
        bool isRoot = false,
        IRelayCommand? navigateCommand = null)
    {
        _name = name;
        _fullPath = fullPath;
        _isLast = isLast;
        _isRoot = isRoot;
        NavigateCommand = navigateCommand;
    }
}

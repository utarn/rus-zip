using CommunityToolkit.Mvvm.ComponentModel;

namespace RusZip.Desktop.ViewModels;

/// <summary>
/// Row view model representing an individual archive format extension in file association lists.
/// </summary>
public partial class FormatAssociationItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeColor))]
    private string _extension = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _currentHandler = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeColor))]
    private bool _isAssociated;

    [ObservableProperty]
    private bool _isSelected = true;

    public string StatusText => IsAssociated
        ? "Default Handler"
        : (string.IsNullOrEmpty(CurrentHandler) ? "Not Associated" : $"Handled by {CurrentHandler}");

    public string StatusBadgeColor => IsAssociated ? "#107C41" : "#D83B01";
}

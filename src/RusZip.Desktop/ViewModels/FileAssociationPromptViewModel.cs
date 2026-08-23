using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Desktop.Services;

namespace RusZip.Desktop.ViewModels;

/// <summary>
/// View model driving the startup prompt dialog when supported archive formats are unassociated.
/// </summary>
public partial class FileAssociationPromptViewModel : ObservableObject
{
    private readonly IFileAssociationService _associationService;

    [ObservableProperty]
    private ObservableCollection<FormatAssociationItemViewModel> _formats = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public event Action? CloseRequested;

    public FileAssociationPromptViewModel(IFileAssociationService associationService)
    {
        _associationService = associationService;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var associations = await _associationService.GetAssociationsAsync(cancellationToken);
            Formats.Clear();
            foreach (var a in associations)
            {
                Formats.Add(new FormatAssociationItemViewModel
                {
                    Extension = a.Extension,
                    DisplayName = a.FormatDisplayName,
                    CurrentHandler = a.CurrentHandler ?? string.Empty,
                    IsAssociated = a.IsAssociated,
                    IsSelected = true // Checked by default
                });
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SetAsDefaultAsync()
    {
        var selected = Formats.Where(f => f.IsSelected).Select(f => f.Extension).ToList();
        if (selected.Count > 0)
        {
            IsBusy = true;
            try
            {
                await _associationService.RegisterAssociationsAsync(selected);
            }
            finally
            {
                IsBusy = false;
            }
        }

        CloseRequested?.Invoke();
    }

    [RelayCommand]
    public void NotNow()
    {
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var format in Formats)
        {
            format.IsSelected = true;
        }
    }

    [RelayCommand]
    public void SelectNone()
    {
        foreach (var format in Formats)
        {
            format.IsSelected = false;
        }
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Desktop.Services;

namespace RusZip.Desktop.ViewModels;

/// <summary>
/// View model driving the desktop settings view and file associations management panel.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IFileAssociationService _associationService;

    [ObservableProperty]
    private ObservableCollection<FormatAssociationItemViewModel> _formats = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _allFormatsAssociated;

    public SettingsViewModel(IFileAssociationService associationService)
    {
        _associationService = associationService;
    }

    [RelayCommand]
    public async Task LoadAssociationsAsync(CancellationToken cancellationToken = default)
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
                    IsSelected = true
                });
            }

            AllFormatsAssociated = await _associationService.AreAllFormatsAssociatedAsync(cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ApplyAssociationsAsync()
    {
        var selected = Formats.Where(f => f.IsSelected).Select(f => f.Extension).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No formats selected.";
            return;
        }

        IsBusy = true;
        try
        {
            await _associationService.RegisterAssociationsAsync(selected);
            StatusMessage = $"Applied associations for {selected.Count} format(s).";
            await LoadAssociationsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to apply associations: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ReapplyAllAssociationsAsync()
    {
        IsBusy = true;
        try
        {
            await _associationService.RegisterDefaultAssociationsAsync();
            StatusMessage = "All supported formats associated with rus-zip.";
            await LoadAssociationsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to reapply associations: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public Task ReapplyAllDefaultsAsync() => ReapplyAllAssociationsAsync();

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

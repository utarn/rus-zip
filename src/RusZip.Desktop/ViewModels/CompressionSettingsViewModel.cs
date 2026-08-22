using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Desktop.Models;

namespace RusZip.Desktop.ViewModels;

public partial class CompressionSettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> AvailableFormats = [".zrus", ".zip"];

    public static readonly IReadOnlyList<CompressionPreset> PresetProfiles =
    [
        new CompressionPreset(3, "Fast", "~50%", "Fastest", "#28A745", "Level 1–5: High-speed compression, minimal CPU usage. Best for fast packaging."),
        new CompressionPreset(9, "Balanced", "~65%", "Balanced", "#0078D4", "Level 6–11: Optimal balance between compression ratio and speed (Default: Level 9)."),
        new CompressionPreset(15, "High", "~75%", "High Ratio", "#E67E22", "Level 12–18: High compression ratio for distribution and storage savings."),
        new CompressionPreset(22, "Ultra", "~80%", "Maximum", "#D83B01", "Level 19–22: Maximum Zstandard compression. High memory and CPU utilization.")
    ];

    public IReadOnlyList<CompressionPreset> Presets => PresetProfiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileName))]
    [NotifyPropertyChangedFor(nameof(ProfileBadgeColor))]
    [NotifyPropertyChangedFor(nameof(ProfileDescription))]
    [NotifyPropertyChangedFor(nameof(ActivePreset))]
    [NotifyPropertyChangedFor(nameof(IsFastSelected))]
    [NotifyPropertyChangedFor(nameof(IsBalancedSelected))]
    [NotifyPropertyChangedFor(nameof(IsHighSelected))]
    [NotifyPropertyChangedFor(nameof(IsUltraSelected))]
    [NotifyPropertyChangedFor(nameof(IsCustomSelected))]
    [NotifyPropertyChangedFor(nameof(CurrentRatioEstimate))]
    [NotifyPropertyChangedFor(nameof(CurrentThroughputEstimate))]
    private int _compressionLevel = 9;

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _destinationPath = string.Empty;
    [ObservableProperty] private string _selectedFormat = ".zrus";

    public IReadOnlyList<string> Formats => AvailableFormats;

    public Func<Task<string?>>? RequestSourceFile { get; set; }
    public Func<Task<string?>>? RequestSourceFolder { get; set; }
    public Func<Task<string?>>? RequestDestinationFile { get; set; }

    public string ProfileName => CompressionLevel switch
    {
        <= 5 => "Fast",
        <= 11 => "Balanced",
        <= 18 => "High",
        _ => "Ultra"
    };

    public string ProfileBadgeColor => CompressionLevel switch
    {
        <= 5 => "#28A745",   // Green (Fast)
        <= 11 => "#0078D4",  // Blue (Balanced)
        <= 18 => "#E67E22",  // Orange (High)
        _ => "#D83B01"       // Flame Red (Ultra)
    };

    public string ProfileDescription => CompressionLevel switch
    {
        <= 5 => "Level 1–5: High-speed compression, minimal CPU usage. Best for fast packaging.",
        <= 11 => "Level 6–11: Optimal balance between compression ratio and speed (Default: Level 9).",
        <= 18 => "Level 12–18: High compression ratio for distribution and storage savings.",
        _ => "Level 19–22: Maximum Zstandard compression. High memory and CPU utilization."
    };

    public string? ActivePreset => CompressionLevel switch
    {
        3 => "Fast",
        9 => "Balanced",
        15 => "High",
        22 => "Ultra",
        _ => null
    };

    public bool IsFastSelected => CompressionLevel == 3;
    public bool IsBalancedSelected => CompressionLevel == 9;
    public bool IsHighSelected => CompressionLevel == 15;
    public bool IsUltraSelected => CompressionLevel == 22;
    public bool IsCustomSelected => !IsFastSelected && !IsBalancedSelected && !IsHighSelected && !IsUltraSelected;

    public string CurrentRatioEstimate => CompressionLevel switch
    {
        <= 5 => "~50%",
        <= 11 => "~65%",
        <= 18 => "~75%",
        _ => "~80%"
    };

    public string CurrentThroughputEstimate => CompressionLevel switch
    {
        <= 5 => "Fastest",
        <= 11 => "Balanced",
        <= 18 => "High Ratio",
        _ => "Maximum"
    };

    partial void OnCompressionLevelChanged(int value)
    {
        if (value < 1)
        {
            CompressionLevel = 1;
        }
        else if (value > 22)
        {
            CompressionLevel = 22;
        }
    }

    partial void OnSourcePathChanged(string? oldValue, string newValue)
    {
        if (string.IsNullOrWhiteSpace(newValue))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(DestinationPath) ||
            (!string.IsNullOrEmpty(oldValue) && (DestinationPath == oldValue + SelectedFormat || DestinationPath == oldValue + (SelectedFormat == ".zrus" ? ".zip" : ".zrus"))))
        {
            DestinationPath = newValue + SelectedFormat;
        }
    }

    partial void OnSelectedFormatChanged(string? oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(DestinationPath))
        {
            if (!string.IsNullOrEmpty(SourcePath))
            {
                DestinationPath = SourcePath + newValue;
            }
            return;
        }

        if (!string.IsNullOrEmpty(oldValue) && DestinationPath.EndsWith(oldValue, StringComparison.OrdinalIgnoreCase))
        {
            DestinationPath = DestinationPath[..^oldValue.Length] + newValue;
        }
        else
        {
            foreach (var fmt in AvailableFormats)
            {
                if (DestinationPath.EndsWith(fmt, StringComparison.OrdinalIgnoreCase))
                {
                    DestinationPath = DestinationPath[..^fmt.Length] + newValue;
                    break;
                }
            }
        }
    }

    [RelayCommand]
    public void SetPreset(int level)
    {
        CompressionLevel = Math.Clamp(level, 1, 22);
    }

    [RelayCommand]
    public void SelectPreset(object? parameter)
    {
        if (parameter is int level)
        {
            SetPreset(level);
        }
        else if (parameter is string name)
        {
            SelectPresetByName(name);
        }
        else if (parameter is CompressionPreset preset)
        {
            SetPreset(preset.Level);
        }
    }

    public void SelectPreset(int level) => SetPreset(level);

    public void SelectPreset(string name) => SelectPresetByName(name);

    private void SelectPresetByName(string name)
    {
        if (string.Equals(name, "Fast", StringComparison.OrdinalIgnoreCase))
        {
            SetPreset(3);
        }
        else if (string.Equals(name, "Balanced", StringComparison.OrdinalIgnoreCase))
        {
            SetPreset(9);
        }
        else if (string.Equals(name, "High", StringComparison.OrdinalIgnoreCase))
        {
            SetPreset(15);
        }
        else if (string.Equals(name, "Ultra", StringComparison.OrdinalIgnoreCase))
        {
            SetPreset(22);
        }
    }

    [RelayCommand]
    public async Task BrowseSourceFileAsync()
    {
        if (RequestSourceFile != null)
        {
            var path = await RequestSourceFile.Invoke();
            if (!string.IsNullOrEmpty(path))
            {
                SourcePath = path;
            }
        }
    }

    [RelayCommand]
    public async Task BrowseSourceFolderAsync()
    {
        if (RequestSourceFolder != null)
        {
            var path = await RequestSourceFolder.Invoke();
            if (!string.IsNullOrEmpty(path))
            {
                SourcePath = path;
            }
        }
    }

    [RelayCommand]
    public async Task BrowseDestinationFileAsync()
    {
        if (RequestDestinationFile != null)
        {
            var path = await RequestDestinationFile.Invoke();
            if (!string.IsNullOrEmpty(path))
            {
                DestinationPath = path;
            }
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RusZip.Desktop.ViewModels;

public partial class CompressionSettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> AvailableFormats = [".zrus", ".zip"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileName))]
    [NotifyPropertyChangedFor(nameof(ProfileBadgeColor))]
    [NotifyPropertyChangedFor(nameof(ProfileDescription))]
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

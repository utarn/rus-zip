using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RusZip.Desktop.ViewModels;

public partial class CompressionSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileName))]
    [NotifyPropertyChangedFor(nameof(ProfileBadgeColor))]
    [NotifyPropertyChangedFor(nameof(ProfileDescription))]
    private int _compressionLevel = 9;

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _destinationPath = string.Empty;
    [ObservableProperty] private string _selectedFormat = ".zrus";

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

    [RelayCommand]
    private void SetPreset(int level)
    {
        CompressionLevel = level;
    }
}

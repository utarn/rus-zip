using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RusZip.Desktop.ViewModels;

public enum ArchiveItemType
{
    Directory,
    File
}

public partial class ArchiveItemViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _relativePath = string.Empty;
    [ObservableProperty] private ArchiveItemType _itemType;
    [ObservableProperty] private long _uncompressedSize;
    [ObservableProperty] private long? _compressedSize;
    [ObservableProperty] private DateTimeOffset? _lastModified;
    [ObservableProperty] private string _attributes = string.Empty;
    [ObservableProperty] private bool _isExpanded;

    public ObservableCollection<ArchiveItemViewModel> Children { get; } = [];
    public bool HasChildren => Children.Count > 0;
    public bool IsDirectory => ItemType == ArchiveItemType.Directory;

    public string FormattedUncompressedSize => IsDirectory ? "-" : FormatBytes(UncompressedSize);
    public string FormattedCompressedSize => (IsDirectory || !CompressedSize.HasValue) ? "-" : FormatBytes(CompressedSize.Value);
    public string FormattedRatio => (IsDirectory || UncompressedSize == 0 || !CompressedSize.HasValue)
        ? "-"
        : $"{((double)CompressedSize.Value / UncompressedSize * 100):0.0}%";

    public string FormattedLastModified => LastModified.HasValue ? LastModified.Value.ToString("yyyy-MM-dd HH:mm") : "-";

    public string IconDisplay => IsDirectory ? "📁" : GetFileIcon(Name);

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] suffixes = ["B", "KB", "MB", "GB", "TB", "PB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024, 2) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:0.##} {suffixes[counter]}";
    }

    public static string GetFileIcon(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".zrus" or ".zip" or ".tar" or ".gz" or ".tgz" or ".7z" or ".rar" or ".bz2" or ".xz" => "📦",
            ".cs" or ".rs" or ".py" or ".js" or ".ts" or ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".html" or ".css" or ".sh" or ".bash" => "📝",
            ".png" or ".jpg" or ".jpeg" or ".svg" or ".webp" or ".gif" or ".bmp" or ".ico" => "🖼️",
            ".pdf" or ".doc" or ".docx" or ".txt" or ".md" or ".rtf" or ".csv" or ".xlsx" => "📄",
            ".exe" or ".dll" or ".so" or ".dylib" or ".bin" => "⚙️",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".mp4" or ".mkv" or ".avi" or ".mov" => "🎬",
            _ => "📄"
        };
    }
}

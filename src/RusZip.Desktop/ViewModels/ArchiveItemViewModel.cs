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

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:0.##} {suffixes[counter]}";
    }

    private static string GetFileIcon(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".zrus" or ".zip" or ".tar" or ".gz" or ".7z" or ".rar" => "📦",
            ".cs" or ".rs" or ".py" or ".js" or ".ts" or ".json" or ".xml" => "📝",
            ".png" or ".jpg" or ".jpeg" or ".svg" or ".webp" or ".gif" => "🖼️",
            ".pdf" or ".doc" or ".docx" or ".txt" or ".md" => "📄",
            _ => "📄"
        };
    }
}

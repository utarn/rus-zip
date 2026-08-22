using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RusZip.Core.Models;

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

    public string FormattedUncompressedSize => IsDirectory ? "-" : DataMetricsFormatter.FormatBytes(UncompressedSize);
    public string FormattedCompressedSize => (IsDirectory || !CompressedSize.HasValue) ? "-" : DataMetricsFormatter.FormatBytes(CompressedSize.Value);
    public string FormattedRatio => IsDirectory ? "-" : DataMetricsFormatter.FormatRatio(CompressedSize, UncompressedSize);

    public string FormattedLastModified => LastModified.HasValue ? LastModified.Value.ToString("yyyy-MM-dd HH:mm") : "-";

    public string IconDisplay => IsDirectory ? "📁" : GetFileIcon(Name);

    public string IconKey => IsDirectory ? "Icon.Folder" : GetIconKey(Name);

    public StreamGeometry? IconGeometry
    {
        get
        {
            if (Application.Current != null && Application.Current.TryGetResource(IconKey, null, out var res) && res is StreamGeometry geom)
            {
                return geom;
            }
            return null;
        }
    }

    public static string GetFileIcon(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".zrus" or ".zip" or ".tar" or ".gz" or ".tgz" or ".7z" or ".rar" or ".bz2" or ".xz" or ".cab" or ".iso" or ".7zip" or ".tbz2" or ".txz" => "📦",
            ".cs" or ".rs" or ".py" or ".js" or ".ts" or ".jsx" or ".tsx" or ".vue" or ".svelte" or ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".html" or ".css" or ".scss" or ".sass" or ".less" or ".sh" or ".bash" or ".zsh" or ".cpp" or ".c" or ".cc" or ".cxx" or ".h" or ".hpp" or ".hh" or ".go" or ".java" or ".kt" or ".kts" or ".swift" or ".php" or ".rb" or ".lua" or ".m" or ".mm" or ".scala" or ".sql" or ".ps1" or ".bat" or ".cmd" => "📝",
            ".png" or ".jpg" or ".jpeg" or ".svg" or ".webp" or ".gif" or ".bmp" or ".ico" or ".tiff" or ".tif" or ".heic" or ".avif" => "🖼️",
            ".pdf" or ".doc" or ".docx" or ".txt" or ".md" or ".rtf" or ".log" or ".csv" or ".odt" or ".xlsx" or ".xls" or ".pptx" or ".ppt" => "📄",
            ".exe" or ".dll" or ".so" or ".dylib" or ".bin" => "⚙️",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".mp4" or ".mkv" or ".avi" or ".mov" => "🎬",
            _ => "📄"
        };
    }

    public static string GetIconKey(string fileName, bool isDirectory = false)
    {
        if (isDirectory) return "Icon.Folder";
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".cs" or ".rs" or ".py" or ".js" or ".ts" or ".jsx" or ".tsx" or ".vue" or ".svelte" or
            ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".html" or ".css" or ".scss" or ".sass" or ".less" or
            ".sh" or ".bash" or ".zsh" or ".cpp" or ".c" or ".cc" or ".cxx" or ".h" or ".hpp" or ".hh" or
            ".go" or ".java" or ".kt" or ".kts" or ".swift" or ".php" or ".rb" or ".lua" or ".m" or ".mm" or
            ".scala" or ".sql" or ".ps1" or ".bat" or ".cmd" => "Icon.FileCode",

            ".txt" or ".md" or ".pdf" or ".doc" or ".docx" or ".rtf" or ".log" or ".csv" or ".odt" or
            ".xlsx" or ".xls" or ".pptx" or ".ppt" => "Icon.FileDoc",

            ".png" or ".jpg" or ".jpeg" or ".svg" or ".webp" or ".gif" or ".bmp" or ".ico" or
            ".tiff" or ".tif" or ".heic" or ".avif" => "Icon.FileImage",

            ".zrus" or ".zip" or ".tar" or ".gz" or ".tgz" or ".7z" or ".rar" or ".bz2" or ".xz" or
            ".cab" or ".iso" or ".7zip" or ".tbz2" or ".txz" => "Icon.FileArchive",

            _ => "Icon.FileGeneric"
        };
    }

    public static ArchiveItemViewModel FromTreeNode(ArchiveTreeNode node, bool autoExpand = false)
    {
        var vm = new ArchiveItemViewModel
        {
            Name = EntryNameSanitizer.Sanitize(node.Name),
            RelativePath = EntryNameSanitizer.Sanitize(node.RelativePath),
            ItemType = node.IsDirectory ? ArchiveItemType.Directory : ArchiveItemType.File,
            UncompressedSize = node.UncompressedSize,
            CompressedSize = node.CompressedSize,
            LastModified = node.LastModified,
            Attributes = EntryNameSanitizer.Sanitize(node.Attributes),
            IsExpanded = autoExpand
        };

        foreach (var child in node.Children)
        {
            vm.Children.Add(FromTreeNode(child, autoExpand));
        }

        return vm;
    }
}

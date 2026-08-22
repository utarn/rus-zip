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
        => FileIconCategorizer.GetFileIcon(fileName);

    public static string GetIconKey(string fileName, bool isDirectory = false)
        => FileIconCategorizer.GetIconKey(fileName, isDirectory);

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

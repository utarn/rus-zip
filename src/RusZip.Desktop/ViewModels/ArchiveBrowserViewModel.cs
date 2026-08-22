using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Core.Models;

namespace RusZip.Desktop.ViewModels;

public partial class ArchiveBrowserViewModel : ObservableObject
{
    private List<ArchiveEntry> _allEntries = [];

    [ObservableProperty] private HierarchicalTreeDataGridSource<ArchiveItemViewModel>? _gridSource;
    [ObservableProperty] private ObservableCollection<ArchiveItemViewModel> _rootItems = [];
    [ObservableProperty] private string _loadedArchivePath = string.Empty;
    [ObservableProperty] private int _totalEntries;
    [ObservableProperty] private long _totalUncompressedBytes;
    [ObservableProperty] private long? _totalCompressedBytes;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private ArchiveItemViewModel? _selectedItem;

    public event Func<Task>? ExtractRequested;

    public string FormattedTotalUncompressedSize => ArchiveItemViewModel.FormatBytes(TotalUncompressedBytes);
    public string FormattedTotalCompressedSize => TotalCompressedBytes.HasValue ? ArchiveItemViewModel.FormatBytes(TotalCompressedBytes.Value) : "-";
    public string FormattedTotalRatio => (TotalUncompressedBytes == 0 || !TotalCompressedBytes.HasValue)
        ? "-"
        : $"{((double)TotalCompressedBytes.Value / TotalUncompressedBytes * 100):0.0}%";

    public void LoadEntries(string archivePath, IReadOnlyList<ArchiveEntry> entries)
    {
        _allEntries = entries.ToList();
        LoadedArchivePath = archivePath;
        TotalEntries = entries.Count;
        TotalUncompressedBytes = entries.Sum(e => e.UncompressedSize);
        TotalCompressedBytes = entries.Any(e => e.CompressedSize.HasValue)
            ? entries.Sum(e => e.CompressedSize ?? 0)
            : null;

        OnPropertyChanged(nameof(FormattedTotalUncompressedSize));
        OnPropertyChanged(nameof(FormattedTotalCompressedSize));
        OnPropertyChanged(nameof(FormattedTotalRatio));

        FilterText = string.Empty;
        RebuildGridSource();
    }

    partial void OnFilterTextChanged(string value)
    {
        RebuildGridSource();
    }

    [RelayCommand]
    public void ClearFilter()
    {
        FilterText = string.Empty;
    }

    [RelayCommand]
    public void ExpandAll()
    {
        SetExpandedRecursive(RootItems, true);
    }

    [RelayCommand]
    public void CollapseAll()
    {
        SetExpandedRecursive(RootItems, false);
    }

    [RelayCommand]
    public async Task RequestExtractAsync()
    {
        if (ExtractRequested != null)
        {
            await ExtractRequested.Invoke();
        }
    }

    private static void SetExpandedRecursive(IEnumerable<ArchiveItemViewModel> items, bool isExpanded)
    {
        foreach (var item in items)
        {
            if (item.IsDirectory)
            {
                item.IsExpanded = isExpanded;
                SetExpandedRecursive(item.Children, isExpanded);
            }
        }
    }

    private void RebuildGridSource()
    {
        IReadOnlyList<ArchiveEntry> entriesToDisplay = _allEntries;
        bool isFiltered = !string.IsNullOrWhiteSpace(FilterText);

        if (isFiltered)
        {
            entriesToDisplay = _allEntries
                .Where(e => e.RelativePath.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        RootItems = BuildTree(entriesToDisplay, autoExpand: isFiltered);
        GridSource = CreateGridSource(RootItems);
    }

    private HierarchicalTreeDataGridSource<ArchiveItemViewModel> CreateGridSource(ObservableCollection<ArchiveItemViewModel> items)
    {
        var nameOptions = new TemplateColumnOptions<ArchiveItemViewModel>
        {
            CanUserSortColumn = true,
            CanUserResizeColumn = true,
            CompareAscending = (a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return -1;
                if (b == null) return 1;
                if (a.IsDirectory != b.IsDirectory)
                    return a.IsDirectory ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            },
            CompareDescending = (a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                if (a.IsDirectory != b.IsDirectory)
                    return a.IsDirectory ? -1 : 1;
                return string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase);
            }
        };

        var uncompressedSizeOptions = new TextColumnOptions<ArchiveItemViewModel>
        {
            CanUserSortColumn = true,
            CanUserResizeColumn = true,
            TextAlignment = Avalonia.Media.TextAlignment.Right,
            CompareAscending = (a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return -1;
                if (b == null) return 1;
                if (a.IsDirectory != b.IsDirectory)
                    return a.IsDirectory ? -1 : 1;
                return a.UncompressedSize.CompareTo(b.UncompressedSize);
            },
            CompareDescending = (a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                if (a.IsDirectory != b.IsDirectory)
                    return a.IsDirectory ? -1 : 1;
                return b.UncompressedSize.CompareTo(a.UncompressedSize);
            }
        };

        var compressedSizeOptions = new TextColumnOptions<ArchiveItemViewModel>
        {
            CanUserSortColumn = true,
            CanUserResizeColumn = true,
            TextAlignment = Avalonia.Media.TextAlignment.Right,
            CompareAscending = (a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return -1;
                if (b == null) return 1;
                if (a.IsDirectory != b.IsDirectory)
                    return a.IsDirectory ? -1 : 1;
                return (a.CompressedSize ?? 0).CompareTo(b.CompressedSize ?? 0);
            },
            CompareDescending = (a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                if (a.IsDirectory != b.IsDirectory)
                    return a.IsDirectory ? -1 : 1;
                return (b.CompressedSize ?? 0).CompareTo(a.CompressedSize ?? 0);
            }
        };

        var ratioOptions = new TextColumnOptions<ArchiveItemViewModel>
        {
            CanUserSortColumn = true,
            CanUserResizeColumn = true,
            TextAlignment = Avalonia.Media.TextAlignment.Right
        };

        var modifiedOptions = new TextColumnOptions<ArchiveItemViewModel>
        {
            CanUserSortColumn = true,
            CanUserResizeColumn = true,
            CompareAscending = (a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return -1;
                if (b == null) return 1;
                if (a.IsDirectory != b.IsDirectory)
                    return a.IsDirectory ? -1 : 1;
                return (a.LastModified ?? DateTimeOffset.MinValue).CompareTo(b.LastModified ?? DateTimeOffset.MinValue);
            },
            CompareDescending = (a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                if (a.IsDirectory != b.IsDirectory)
                    return a.IsDirectory ? -1 : 1;
                return (b.LastModified ?? DateTimeOffset.MinValue).CompareTo(a.LastModified ?? DateTimeOffset.MinValue);
            }
        };

        var attributesOptions = new TextColumnOptions<ArchiveItemViewModel>
        {
            CanUserSortColumn = true,
            CanUserResizeColumn = true,
            CompareAscending = (a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return -1;
                if (b == null) return 1;
                return string.Compare(a.Attributes, b.Attributes, StringComparison.OrdinalIgnoreCase);
            },
            CompareDescending = (a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                return string.Compare(b.Attributes, a.Attributes, StringComparison.OrdinalIgnoreCase);
            }
        };

        var source = new HierarchicalTreeDataGridSource<ArchiveItemViewModel>(items)
        {
            Columns =
            {
                new HierarchicalExpanderColumn<ArchiveItemViewModel>(
                    new TemplateColumn<ArchiveItemViewModel>(
                        "Name",
                        new FuncDataTemplate<ArchiveItemViewModel>((item, _) =>
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 6,
                                Children =
                                {
                                    new PathIcon
                                    {
                                        Width = 16,
                                        Height = 16,
                                        VerticalAlignment = VerticalAlignment.Center,
                                        [!PathIcon.DataProperty] = new Avalonia.Data.Binding(nameof(ArchiveItemViewModel.IconGeometry))
                                    },
                                    new TextBlock
                                    {
                                        VerticalAlignment = VerticalAlignment.Center,
                                        [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(ArchiveItemViewModel.Name))
                                    }
                                }
                            }
                        ),
                        null,
                        new GridLength(1, GridUnitType.Star),
                        nameOptions
                    ),
                    x => x.Children,
                    x => x.HasChildren,
                    x => x.IsExpanded
                ),

                new TextColumn<ArchiveItemViewModel, string>(
                    "Size",
                    x => x.FormattedUncompressedSize,
                    new GridLength(110, GridUnitType.Pixel),
                    uncompressedSizeOptions
                ),

                new TextColumn<ArchiveItemViewModel, string>(
                    "Compressed",
                    x => x.FormattedCompressedSize,
                    new GridLength(110, GridUnitType.Pixel),
                    compressedSizeOptions
                ),

                new TextColumn<ArchiveItemViewModel, string>(
                    "Ratio",
                    x => x.FormattedRatio,
                    new GridLength(80, GridUnitType.Pixel),
                    ratioOptions
                ),

                new TextColumn<ArchiveItemViewModel, string>(
                    "Modified",
                    x => x.FormattedLastModified,
                    new GridLength(140, GridUnitType.Pixel),
                    modifiedOptions
                ),

                new TextColumn<ArchiveItemViewModel, string>(
                    "Attributes",
                    x => x.Attributes,
                    new GridLength(90, GridUnitType.Pixel),
                    attributesOptions
                )
            }
        };

        if (source.RowSelection != null)
        {
            source.RowSelection.SingleSelect = true;
            source.RowSelection.SelectionChanged += (_, e) =>
            {
                SelectedItem = source.RowSelection.SelectedItem;
            };
        }

        return source;
    }

    public static ObservableCollection<ArchiveItemViewModel> BuildTree(IReadOnlyList<ArchiveEntry> entries, bool autoExpand = false)
    {
        var rootNodes = new ObservableCollection<ArchiveItemViewModel>();
        var lookup = new Dictionary<string, ArchiveItemViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries.OrderBy(e => e.RelativePath.Replace('\\', '/')))
        {
            var normalizedPath = entry.RelativePath.Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(normalizedPath)) continue;

            var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string currentPath = string.Empty;
            ArchiveItemViewModel? parent = null;

            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                currentPath = string.IsNullOrEmpty(currentPath) ? segment : $"{currentPath}/{segment}";
                bool isLeaf = (i == segments.Length - 1) && !entry.IsDirectory;

                if (!lookup.TryGetValue(currentPath, out var node))
                {
                    node = new ArchiveItemViewModel
                    {
                        Name = segment,
                        RelativePath = currentPath,
                        ItemType = isLeaf ? ArchiveItemType.File : ArchiveItemType.Directory,
                        UncompressedSize = isLeaf ? entry.UncompressedSize : 0,
                        CompressedSize = isLeaf ? entry.CompressedSize : 0,
                        LastModified = entry.LastModified,
                        Attributes = isLeaf ? entry.Attributes : string.Empty,
                        IsExpanded = autoExpand
                    };

                    lookup[currentPath] = node;

                    if (parent == null)
                        rootNodes.Add(node);
                    else
                        parent.Children.Add(node);
                }
                else
                {
                    if (isLeaf)
                    {
                        node.ItemType = ArchiveItemType.File;
                        node.UncompressedSize = entry.UncompressedSize;
                        node.CompressedSize = entry.CompressedSize;
                        node.LastModified = entry.LastModified;
                        node.Attributes = entry.Attributes;
                    }
                }

                if (!isLeaf && node != null)
                {
                    node.UncompressedSize += entry.UncompressedSize;
                    if (entry.CompressedSize.HasValue)
                    {
                        node.CompressedSize = (node.CompressedSize ?? 0) + entry.CompressedSize.Value;
                    }
                }

                parent = node;
            }
        }

        return rootNodes;
    }
}

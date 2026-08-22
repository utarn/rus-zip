using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Core.Engines;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private string _loadErrorMessage = string.Empty;

    /// <summary>Extraction guardrail settings surfaced in the browser toolbar (ADR-0007).</summary>
    public ExtractionSettingsViewModel ExtractionSettings { get; } = new();

    /// <summary>
    /// Entry-count cap above which tree construction is refused to avoid exhausting memory
    /// (ADR-0007 F-36). Defaults to the extraction entry cap; overridable for testing.
    /// </summary>
    public int EntryCountCap { get; set; } = SafeArchiveExtractor.DefaultMaxEntryCount;

    public bool HasLoadError => !string.IsNullOrEmpty(LoadErrorMessage);

    public ObservableCollection<BreadcrumbItemViewModel> Breadcrumbs { get; } = [];

    public event Func<Task>? ExtractRequested;
    public event Func<ArchiveItemViewModel, Task>? ExtractItemRequested;
    public event Func<string, Task>? CopyPathRequested;

    public Func<string, Task>? CopyToClipboardService { get; set; }

    public string FormattedTotalUncompressedSize => DataMetricsFormatter.FormatBytes(TotalUncompressedBytes);
    public string FormattedTotalCompressedSize => TotalCompressedBytes.HasValue ? DataMetricsFormatter.FormatBytes(TotalCompressedBytes.Value) : "-";
    public string FormattedTotalRatio => DataMetricsFormatter.FormatRatio(TotalCompressedBytes, TotalUncompressedBytes);

    public ArchiveBrowserViewModel()
    {
        UpdateBreadcrumbs(null);
    }

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
        SelectedItem = null;

        // GUI memory guard (ADR-0007 F-36): refuse tree construction beyond the entry-count cap
        // with a clear message instead of exhausting memory.
        if (entries.Count > EntryCountCap)
        {
            LoadErrorMessage =
                $"This archive lists {entries.Count:N0} entries, exceeding the {EntryCountCap:N0}-entry safety limit for browsing. " +
                "It may be a decompression bomb; the tree was not loaded to avoid exhausting memory.";
            RootItems = [];
            GridSource = null;
            UpdateBreadcrumbs(null);
            return;
        }

        LoadErrorMessage = string.Empty;
        RebuildGridSource();
        UpdateBreadcrumbs(null);
    }

    partial void OnFilterTextChanged(string value)
    {
        RebuildGridSource();
    }

    partial void OnSelectedItemChanged(ArchiveItemViewModel? value)
    {
        UpdateBreadcrumbs(value);
        ExtractSelectedItemCommand.NotifyCanExecuteChanged();
        ExtractItemCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        CopySelectedItemPathCommand.NotifyCanExecuteChanged();
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

    [RelayCommand]
    public async Task ExtractSelectedItemAsync()
    {
        if (SelectedItem != null && ExtractItemRequested != null)
        {
            await ExtractItemRequested.Invoke(SelectedItem);
        }
        else if (SelectedItem == null && ExtractRequested != null)
        {
            await ExtractRequested.Invoke();
        }
    }

    [RelayCommand]
    public async Task ExtractItemAsync(ArchiveItemViewModel? item = null)
    {
        var target = item ?? SelectedItem;
        if (target != null && ExtractItemRequested != null)
        {
            await ExtractItemRequested.Invoke(target);
        }
        else if (target == null && ExtractRequested != null)
        {
            await ExtractRequested.Invoke();
        }
    }

    [RelayCommand]
    public async Task CopyPathAsync(object? parameter = null)
    {
        var target = parameter switch
        {
            ArchiveItemViewModel item => item,
            string str => FindItemByPath(str),
            _ => SelectedItem
        };

        if (target == null || string.IsNullOrWhiteSpace(target.RelativePath))
            return;

        var path = target.RelativePath;
        if (CopyToClipboardService != null)
        {
            await CopyToClipboardService(path);
        }
        else
        {
            await CopyToClipboardDefaultAsync(path);
        }

        if (CopyPathRequested != null)
        {
            await CopyPathRequested.Invoke(path);
        }
    }

    [RelayCommand]
    public async Task CopySelectedItemPathAsync()
    {
        await CopyPathAsync(SelectedItem);
    }

    public static async Task CopyToClipboardDefaultAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard != null)
        {
            await desktop.MainWindow.Clipboard.SetTextAsync(text);
        }
    }

    [RelayCommand]
    public void NavigateToBreadcrumb(object? parameter)
    {
        string? path = parameter switch
        {
            BreadcrumbItemViewModel b => b.FullPath,
            string s => s,
            _ => null
        };

        NavigateToPath(path);
    }

    public void NavigateToPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/" || path == "(root)" || path.Equals("archive", StringComparison.OrdinalIgnoreCase))
        {
            SelectedItem = null;
            if (GridSource?.RowSelection != null)
            {
                GridSource.RowSelection.Clear();
            }
            UpdateBreadcrumbs(null);
            return;
        }

        var normalized = path.Replace('\\', '/').Trim('/');
        ExpandAncestorsForPath(normalized);

        var target = FindItemByPath(normalized);
        if (target != null)
        {
            if (target.IsDirectory)
            {
                target.IsExpanded = true;
            }
            SelectedItem = target;
            UpdateBreadcrumbs(target);
        }
    }

    public void UpdateBreadcrumbs(ArchiveItemViewModel? selectedItem)
    {
        Breadcrumbs.Clear();

        var rootItem = new BreadcrumbItemViewModel
        {
            Name = "Archive",
            FullPath = string.Empty,
            IsRoot = true,
            IsLast = (selectedItem == null || string.IsNullOrWhiteSpace(selectedItem.RelativePath)),
            NavigateCommand = NavigateToBreadcrumbCommand
        };
        Breadcrumbs.Add(rootItem);

        if (selectedItem == null || string.IsNullOrWhiteSpace(selectedItem.RelativePath))
        {
            return;
        }

        var normalized = selectedItem.RelativePath.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string accumulated = string.Empty;

        for (int i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            accumulated = string.IsNullOrEmpty(accumulated) ? segment : $"{accumulated}/{segment}";
            bool isLast = (i == segments.Length - 1);

            Breadcrumbs.Add(new BreadcrumbItemViewModel
            {
                Name = segment,
                FullPath = accumulated,
                IsRoot = false,
                IsLast = isLast,
                NavigateCommand = NavigateToBreadcrumbCommand
            });
        }
    }

    public ArchiveItemViewModel? FindItemByPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized == "(root)" || normalized.Equals("archive", StringComparison.OrdinalIgnoreCase))
            return null;

        return FindItemByPathRecursive(RootItems, normalized);
    }

    private static ArchiveItemViewModel? FindItemByPathRecursive(IEnumerable<ArchiveItemViewModel> items, string path)
    {
        foreach (var item in items)
        {
            var itemNormalized = item.RelativePath.Replace('\\', '/').Trim('/');
            if (string.Equals(itemNormalized, path, StringComparison.OrdinalIgnoreCase))
                return item;

            var found = FindItemByPathRecursive(item.Children, path);
            if (found != null)
                return found;
        }
        return null;
    }

    private void ExpandAncestorsForPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string current = string.Empty;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            current = string.IsNullOrEmpty(current) ? segments[i] : $"{current}/{segments[i]}";
            var ancestor = FindItemByPath(current);
            if (ancestor != null && ancestor.IsDirectory)
            {
                ancestor.IsExpanded = true;
            }
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
        bool isFiltered = !string.IsNullOrWhiteSpace(FilterText);
        RootItems = BuildTree(_allEntries, FilterText, autoExpand: isFiltered);
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

    public static ObservableCollection<ArchiveItemViewModel> BuildTree(
        IEnumerable<ArchiveEntry> entries,
        bool autoExpand = false)
    {
        return BuildTree(entries, filterText: null, autoExpand: autoExpand);
    }

    public static ObservableCollection<ArchiveItemViewModel> BuildTree(
        IEnumerable<ArchiveEntry> entries,
        string? filterText,
        bool autoExpand = false)
    {
        var treeNodes = ArchiveHierarchy.BuildTree(entries, filterText);
        var result = new ObservableCollection<ArchiveItemViewModel>();
        foreach (var node in treeNodes)
        {
            result.Add(ArchiveItemViewModel.FromTreeNode(node, autoExpand));
        }
        return result;
    }
}

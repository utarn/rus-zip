using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.DataGridHierarchical;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Core.Engines;
using RusZip.Core.Models;

namespace RusZip.Desktop.ViewModels;

public partial class ArchiveBrowserViewModel : ObservableObject
{
    private List<ArchiveEntry> _allEntries = [];

    [ObservableProperty] private IHierarchicalModel? _gridSource;
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
    /// Column sort comparers consumed by the ProDataGrid column definitions in
    /// <c>ArchiveBrowserView.axaml</c>. Directories sort above files (matching the
    /// TreeDataGrid-era behavior); the grid framework negates these for descending sorts.
    /// </summary>
    public IComparer NameColumnSortComparer { get; } = ArchiveItemComparer.CreateName();
    public IComparer SizeColumnSortComparer { get; } = ArchiveItemComparer.CreateSize();
    public IComparer CompressedColumnSortComparer { get; } = ArchiveItemComparer.CreateCompressed();
    public IComparer RatioColumnSortComparer { get; } = ArchiveItemComparer.CreateRatio();
    public IComparer ModifiedColumnSortComparer { get; } = ArchiveItemComparer.CreateModified();
    public IComparer AttributesColumnSortComparer { get; } = ArchiveItemComparer.CreateAttributes();

    /// <summary>
    /// Entry-count cap above which tree construction is refused to avoid exhausting memory
    /// (ADR-0007 F-36). Defaults to the extraction entry cap; overridable for testing.
    /// </summary>
    public int EntryCountCap { get; set; } = SafeArchiveExtractor.DefaultMaxEntryCount;

    public bool HasLoadError => !string.IsNullOrEmpty(LoadErrorMessage);

    public ObservableCollection<BreadcrumbItemViewModel> Breadcrumbs { get; } = [];

    /// <summary>
    /// Most recently expanded directory, used to keep the breadcrumbs in sync with tree
    /// expansion (F-39) when no item is explicitly selected. ProDataGrid drives expansion by
    /// writing <see cref="ArchiveItemViewModel.IsExpanded"/>, so reacting to that property's
    /// change notification keeps breadcrumbs as pure VM state — no view events required.
    /// </summary>
    private ArchiveItemViewModel? _expansionAnchor;

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
        UpdateBreadcrumbsFromNavigation();
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
            UpdateBreadcrumbsFromNavigation();
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
            UpdateBreadcrumbsFromNavigation();
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
                Name = EntryNameSanitizer.Sanitize(segment),
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
        _expansionAnchor = null;
        bool isFiltered = !string.IsNullOrWhiteSpace(FilterText);
        RootItems = BuildTree(_allEntries, FilterText, autoExpand: isFiltered);
        GridSource = CreateGridSource(RootItems);
        HookExpansionNotifications(RootItems);
        UpdateBreadcrumbsFromNavigation();
    }

    /// <summary>
    /// Subscribes every tree node's <see cref="System.ComponentModel.INotifyPropertyChanged"/>
    /// so expansion/collapse (which ProDataGrid drives by writing
    /// <see cref="ArchiveItemViewModel.IsExpanded"/>) keeps the breadcrumbs current.
    /// </summary>
    private void HookExpansionNotifications(ObservableCollection<ArchiveItemViewModel> roots)
    {
        foreach (var root in roots)
        {
            HookExpansionNotificationsRecursive(root);
        }
    }

    private void HookExpansionNotificationsRecursive(ArchiveItemViewModel item)
    {
        item.PropertyChanged -= OnArchiveItemPropertyChanged;
        item.PropertyChanged += OnArchiveItemPropertyChanged;
        foreach (var child in item.Children)
        {
            HookExpansionNotificationsRecursive(child);
        }
    }

    private void OnArchiveItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ArchiveItemViewModel item || e.PropertyName != nameof(ArchiveItemViewModel.IsExpanded))
        {
            return;
        }

        if (item.IsExpanded)
        {
            _expansionAnchor = item;
        }
        else if (IsSelfOrDescendant(item, _expansionAnchor))
        {
            // The collapsed node (or a subtree under it) was the navigation anchor; walk up to
            // the closest ancestor that is still expanded, falling back to the archive root.
            _expansionAnchor = FindDeepestExpandedAncestor(item);
        }

        UpdateBreadcrumbsFromNavigation();
    }

    private static bool IsSelfOrDescendant(ArchiveItemViewModel? ancestor, ArchiveItemViewModel? node)
    {
        var current = node;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
            current = current.Parent;
        }
        return false;
    }

    private static ArchiveItemViewModel? FindDeepestExpandedAncestor(ArchiveItemViewModel node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current.IsDirectory && current.IsExpanded)
            {
                return current;
            }
            current = current.Parent;
        }
        return null;
    }

    /// <summary>
    /// The breadcrumb trail reflects the current navigation context: the selected node when one
    /// is chosen, otherwise the most recently expanded directory (F-39).
    /// </summary>
    private void UpdateBreadcrumbsFromNavigation()
    {
        UpdateBreadcrumbs(SelectedItem ?? _expansionAnchor);
    }

    private IHierarchicalModel CreateGridSource(ObservableCollection<ArchiveItemViewModel> items)
    {
        var model = new HierarchicalModel(new HierarchicalOptions
        {
            ChildrenPropertyPath = nameof(ArchiveItemViewModel.Children),
            IsExpandedPropertyPath = nameof(ArchiveItemViewModel.IsExpanded),
            VirtualizeChildren = true
        });
        model.SetRoots(items);
        return model;
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

using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Input.Platform;
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
    [ObservableProperty] private FileCategory _selectedCategory = FileCategory.All;
    [ObservableProperty] private ArchiveItemViewModel? _selectedItem;
    [ObservableProperty] private bool _canCompress;

    public bool IsCategoryAll => SelectedCategory == FileCategory.All;
    public bool IsCategoryDocuments => SelectedCategory == FileCategory.Documents;
    public bool IsCategoryImages => SelectedCategory == FileCategory.Images;
    public bool IsCategoryCode => SelectedCategory == FileCategory.Code;
    public bool IsCategoryMedia => SelectedCategory == FileCategory.Media;
    public bool IsCategoryArchives => SelectedCategory == FileCategory.Archives;

    public ObservableCollection<ArchiveItemViewModel> SelectedItems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private string _loadErrorMessage = string.Empty;

    /// <summary>Extraction guardrail settings surfaced in the browser toolbar (ADR-0007).</summary>
    public ExtractionSettingsViewModel ExtractionSettings { get; } = new();

    /// <summary>
    /// Column sort comparers consumed by the ProDataGrid column definitions in
    /// <c>ArchiveBrowserView.axaml</c>. The comparers are direction-aware so directories stay
    /// grouped above files in BOTH ascending and descending sorts (spec #57 user story 2).
    /// </summary>
    public ArchiveItemSortComparer NameColumnSortComparer { get; } = ArchiveItemComparer.CreateName();
    public ArchiveItemSortComparer SizeColumnSortComparer { get; } = ArchiveItemComparer.CreateSize();
    public ArchiveItemSortComparer CompressedColumnSortComparer { get; } = ArchiveItemComparer.CreateCompressed();
    public ArchiveItemSortComparer RatioColumnSortComparer { get; } = ArchiveItemComparer.CreateRatio();
    public ArchiveItemSortComparer ModifiedColumnSortComparer { get; } = ArchiveItemComparer.CreateModified();
    public ArchiveItemSortComparer AttributesColumnSortComparer { get; } = ArchiveItemComparer.CreateAttributes();

    /// <summary>
    /// Pushes the active column's sort direction into every column comparer. ProDataGrid's
    /// sort framework sets the column's <c>SortDirection</c> before invoking the column
    /// comparer and negates the comparer result for descending sorts; the comparers invert
    /// their directory grouping for descending so directories stay above files in both
    /// directions. The view code-behind calls this from each column's SortDirection changes.
    /// </summary>
    public void SetColumnSortDirection(ListSortDirection? direction)
    {
        var d = direction ?? ListSortDirection.Ascending;
        NameColumnSortComparer.Direction = d;
        SizeColumnSortComparer.Direction = d;
        CompressedColumnSortComparer.Direction = d;
        RatioColumnSortComparer.Direction = d;
        ModifiedColumnSortComparer.Direction = d;
        AttributesColumnSortComparer.Direction = d;
    }

    /// <summary>
    /// Entry-count cap above which tree construction is refused to avoid exhausting memory
    /// (ADR-0007 F-36). Defaults to the extraction entry cap; overridable for testing.
    /// </summary>
    public int EntryCountCap { get; set; } = SafeArchiveExtractor.DefaultMaxEntryCount;

    /// <summary>
    /// Factory used to materialize <see cref="ArchiveItemViewModel"/> tree nodes. Defaults to
    /// <see cref="ArchiveItemViewModel.FromTreeNode"/>; tests override it to count allocations and
    /// prove the entry-count cap (ADR-0007 F-36) is enforced before any tree node / row model is
    /// ever allocated.
    /// </summary>
    internal Func<ArchiveTreeNode, bool, ArchiveItemViewModel>? ItemFactory { get; set; }

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
    public event Func<IReadOnlyList<ArchiveItemViewModel>, Task>? ExtractItemsRequested;
    public event Func<Task>? AppendRequested;
    public event Func<IReadOnlyList<ArchiveItemViewModel>, Task>? DeleteRequested;
    public event Func<string, Task>? CopyPathRequested;
    public event Func<ArchiveItemViewModel, Task>? PreviewItemRequested;
    public event Func<ArchiveItemViewModel?, Task>? PropertiesRequested;

    public Func<int, IReadOnlyList<string>, Task<bool>>? ConfirmDeleteAsync { get; set; }
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
        LoadedArchivePath = archivePath;
        TotalEntries = entries.Count;
        TotalUncompressedBytes = entries.Sum(e => e.UncompressedSize);
        TotalCompressedBytes = entries.Any(e => e.CompressedSize.HasValue)
            ? entries.Sum(e => e.CompressedSize ?? 0)
            : null;

        OnPropertyChanged(nameof(FormattedTotalUncompressedSize));
        OnPropertyChanged(nameof(FormattedTotalCompressedSize));
        OnPropertyChanged(nameof(FormattedTotalRatio));

        // GUI memory guard (ADR-0007 F-36): refuse tree construction beyond the entry-count cap
        // with a clear message instead of exhausting memory. This check MUST run before
        // _allEntries is retained and before the filter is reset — resetting the filter fires
        // OnFilterTextChanged -> RebuildGridSource, and a later check would let a hostile
        // archive's tree (ArchiveItemViewModels, the HierarchicalModel, and row structures)
        // materialize first. On the abort path _allEntries is dropped and RebuildGridSource
        // additionally no-ops while HasLoadError is set, so the tree can never be rebuilt from
        // the hostile list afterwards either.
        if (entries.Count > EntryCountCap)
        {
            CanCompress = false;
            _allEntries = [];
            LoadErrorMessage =
                $"This archive lists {entries.Count:N0} entries, exceeding the {EntryCountCap:N0}-entry safety limit for browsing. " +
                "It may be a decompression bomb; the tree was not loaded to avoid exhausting memory.";
            RootItems = [];
            GridSource = null;
            FilterText = string.Empty;
            SelectedItem = null;
            SelectedItems.Clear();
            UpdateBreadcrumbs(null);
            return;
        }

        CanCompress = ArchiveFormatRegistry.TryDetect(archivePath, out var descriptor)
            && descriptor.CanCompress
            && descriptor.Format != ArchiveFormat.Zst;

        _allEntries = entries.ToList();
        LoadErrorMessage = string.Empty;
        FilterText = string.Empty;
        SelectedItem = null;
        SelectedItems.Clear();
        RebuildGridSource();
        UpdateBreadcrumbs(null);
    }

    partial void OnCanCompressChanged(bool value)
    {
        AppendFilesCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnFilterTextChanged(string value)
    {
        RebuildGridSource();
    }

    partial void OnSelectedCategoryChanged(FileCategory value)
    {
        OnPropertyChanged(nameof(IsCategoryAll));
        OnPropertyChanged(nameof(IsCategoryDocuments));
        OnPropertyChanged(nameof(IsCategoryImages));
        OnPropertyChanged(nameof(IsCategoryCode));
        OnPropertyChanged(nameof(IsCategoryMedia));
        OnPropertyChanged(nameof(IsCategoryArchives));
        RebuildGridSource();
    }

    [RelayCommand]
    public void SetCategory(object? parameter)
    {
        if (parameter is FileCategory cat)
        {
            SelectedCategory = cat;
        }
        else if (parameter is string catName && Enum.TryParse<FileCategory>(catName, ignoreCase: true, out var parsed))
        {
            SelectedCategory = parsed;
        }
    }

    partial void OnSelectedItemChanged(ArchiveItemViewModel? value)
    {
        UpdateBreadcrumbsFromNavigation();
        NotifySelectionCommands();
    }

    public void SetSelectedItems(IEnumerable<ArchiveItemViewModel> items)
    {
        SelectedItems.Clear();
        foreach (var item in items)
        {
            SelectedItems.Add(item);
        }
        NotifySelectionCommands();
    }

    public IReadOnlyList<ArchiveItemViewModel> GetEffectiveSelectedItems(ArchiveItemViewModel? contextTarget = null)
    {
        if (contextTarget != null)
        {
            if (SelectedItems.Contains(contextTarget))
            {
                return SelectedItems.ToList();
            }
            return [contextTarget];
        }

        if (SelectedItems.Count > 0)
        {
            return SelectedItems.ToList();
        }

        if (SelectedItem != null)
        {
            return [SelectedItem];
        }

        return [];
    }

    private void NotifySelectionCommands()
    {
        ExtractSelectedItemCommand.NotifyCanExecuteChanged();
        ExtractItemCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        CopySelectedItemPathCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    public void ClearFilter()
    {
        FilterText = string.Empty;
    }

    [RelayCommand]
    public void SelectAll()
    {
        var allItems = GetAllFlatItems(RootItems);
        SelectedItems.Clear();
        foreach (var item in allItems)
        {
            SelectedItems.Add(item);
        }
        if (allItems.Count > 0)
        {
            SelectedItem = allItems[0];
        }
        NotifySelectionCommands();
    }

    [RelayCommand]
    public void InvertSelection()
    {
        var allItems = GetAllFlatItems(RootItems);
        var currentlySelected = SelectedItems.ToHashSet();
        SelectedItems.Clear();
        foreach (var item in allItems)
        {
            if (!currentlySelected.Contains(item))
            {
                SelectedItems.Add(item);
            }
        }
        SelectedItem = SelectedItems.FirstOrDefault();
        NotifySelectionCommands();
    }

    public static List<ArchiveItemViewModel> GetAllFlatItems(IEnumerable<ArchiveItemViewModel> items)
    {
        var list = new List<ArchiveItemViewModel>();
        foreach (var item in items)
        {
            list.Add(item);
            list.AddRange(GetAllFlatItems(item.Children));
        }
        return list;
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

    [RelayCommand(CanExecute = nameof(CanExecuteAppend))]
    public async Task AppendFilesAsync()
    {
        if (AppendRequested != null)
        {
            await AppendRequested.Invoke();
        }
    }

    public bool CanExecuteAppend() => CanCompress;

    [RelayCommand(CanExecute = nameof(CanExecuteDelete))]
    public async Task DeleteSelectedAsync(object? parameter = null)
    {
        var target = parameter switch
        {
            ArchiveItemViewModel single => GetEffectiveSelectedItems(single),
            IEnumerable<ArchiveItemViewModel> multiple => multiple.ToList(),
            _ => GetEffectiveSelectedItems()
        };

        if (target.Count == 0)
            return;

        var paths = target.Select(t => t.RelativePath).ToList();

        if (ConfirmDeleteAsync != null)
        {
            var confirmed = await ConfirmDeleteAsync(target.Count, paths);
            if (!confirmed)
                return;
        }

        if (DeleteRequested != null)
        {
            await DeleteRequested.Invoke(target);
        }
    }

    public bool CanExecuteDelete(object? parameter = null)
    {
        if (!CanCompress) return false;
        if (parameter is ArchiveItemViewModel) return true;
        if (parameter is IEnumerable<ArchiveItemViewModel> list) return list.Any();
        return SelectedItem != null || SelectedItems.Count > 0;
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
        var targets = GetEffectiveSelectedItems();
        if (targets.Count > 1 && ExtractItemsRequested != null)
        {
            await ExtractItemsRequested.Invoke(targets);
        }
        else if (targets.Count == 1 && ExtractItemRequested != null)
        {
            await ExtractItemRequested.Invoke(targets[0]);
        }
        else if (targets.Count == 0 && ExtractRequested != null)
        {
            await ExtractRequested.Invoke();
        }
    }

    [RelayCommand]
    public async Task ExtractItemAsync(object? parameter = null)
    {
        var target = parameter switch
        {
            ArchiveItemViewModel single => GetEffectiveSelectedItems(single),
            IEnumerable<ArchiveItemViewModel> multiple => multiple.ToList(),
            _ => GetEffectiveSelectedItems()
        };

        if (target.Count > 1 && ExtractItemsRequested != null)
        {
            await ExtractItemsRequested.Invoke(target);
        }
        else if (target.Count == 1 && ExtractItemRequested != null)
        {
            await ExtractItemRequested.Invoke(target[0]);
        }
        else if (target.Count == 0 && ExtractRequested != null)
        {
            await ExtractRequested.Invoke();
        }
    }

    [RelayCommand]
    public async Task PreviewFileAsync(object? parameter = null)
    {
        var target = parameter as ArchiveItemViewModel ?? SelectedItem;
        if (target != null && !target.IsDirectory && PreviewItemRequested != null)
        {
            await PreviewItemRequested.Invoke(target);
        }
    }

    [RelayCommand]
    public async Task ActivateItemAsync(object? parameter = null)
    {
        var target = parameter as ArchiveItemViewModel ?? SelectedItem;
        if (target == null) return;

        if (target.IsDirectory)
        {
            target.IsExpanded = !target.IsExpanded;
        }
        else
        {
            await PreviewFileAsync(target);
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
    public async Task ShowPropertiesAsync(object? parameter = null)
    {
        var target = parameter as ArchiveItemViewModel ?? SelectedItem;
        if (PropertiesRequested != null)
        {
            await PropertiesRequested.Invoke(target);
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
        // GUI memory guard (ADR-0007 F-36): while a load error is showing the tree is refused.
        // Guarding the single rebuild choke point means no caller (filter keystrokes, breadcrumb
        // navigation, etc.) can rebuild a tree from a hostile entry list after the initial cap
        // check in LoadEntries.
        if (HasLoadError)
        {
            return;
        }

        _expansionAnchor = null;
        bool isFiltered = !string.IsNullOrWhiteSpace(FilterText) || SelectedCategory != FileCategory.All;
        Func<string, bool, bool>? categoryPredicate = SelectedCategory == FileCategory.All
            ? null
            : (path, isDir) => isDir || FileIconCategorizer.GetFileCategory(path, isDir) == SelectedCategory;

        RootItems = BuildTree(_allEntries, FilterText, autoExpand: isFiltered, factory: ItemFactory, categoryPredicate: categoryPredicate);
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
        return BuildTree(entries, filterText, autoExpand, factory: null, categoryPredicate: null);
    }

    /// <summary>
    /// Materializes the tree. <paramref name="factory"/> lets tests intercept node creation to
    /// prove the entry-count cap (ADR-0007 F-36) is enforced before any row model is allocated;
    /// when null the default <see cref="ArchiveItemViewModel.FromTreeNode"/> is used.
    /// </summary>
    internal static ObservableCollection<ArchiveItemViewModel> BuildTree(
        IEnumerable<ArchiveEntry> entries,
        string? filterText,
        bool autoExpand,
        Func<ArchiveTreeNode, bool, ArchiveItemViewModel>? factory,
        Func<string, bool, bool>? categoryPredicate = null)
    {
        var treeNodes = ArchiveHierarchy.BuildTree(entries, filterText, categoryPredicate);
        var result = new ObservableCollection<ArchiveItemViewModel>();
        var create = factory ?? ArchiveItemViewModel.FromTreeNode;
        foreach (var node in treeNodes)
        {
            result.Add(create(node, autoExpand));
        }
        return result;
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls.DataGridHierarchical;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Core.Models;
using RusZip.Desktop.Models;

namespace RusZip.Desktop.ViewModels;

public partial class CompressionSettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> AvailableFormats = [".zrus", ".zip"];

    public static readonly IReadOnlyList<CompressionPreset> PresetProfiles =
    [
        new CompressionPreset(3, "Fast", "~50%", "Fastest", "#28A745", "Level 1–5: High-speed compression, minimal CPU usage. Best for fast packaging."),
        new CompressionPreset(9, "Balanced", "~65%", "Balanced", "#0078D4", "Level 6–11: Optimal balance between compression ratio and speed (Default: Level 9)."),
        new CompressionPreset(15, "High", "~75%", "High Ratio", "#E67E22", "Level 12–18: High compression ratio for distribution and storage savings."),
        new CompressionPreset(22, "Ultra", "~80%", "Maximum", "#D83B01", "Level 19–22: Maximum Zstandard compression. High memory and CPU utilization.")
    ];

    public IReadOnlyList<CompressionPreset> Presets => PresetProfiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileName))]
    [NotifyPropertyChangedFor(nameof(ProfileBadgeColor))]
    [NotifyPropertyChangedFor(nameof(ProfileDescription))]
    [NotifyPropertyChangedFor(nameof(ActivePreset))]
    [NotifyPropertyChangedFor(nameof(IsFastSelected))]
    [NotifyPropertyChangedFor(nameof(IsBalancedSelected))]
    [NotifyPropertyChangedFor(nameof(IsHighSelected))]
    [NotifyPropertyChangedFor(nameof(IsUltraSelected))]
    [NotifyPropertyChangedFor(nameof(IsCustomSelected))]
    [NotifyPropertyChangedFor(nameof(CurrentRatioEstimate))]
    [NotifyPropertyChangedFor(nameof(CurrentThroughputEstimate))]
    private int _compressionLevel = 9;

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _destinationPath = string.Empty;
    [ObservableProperty] private string _selectedFormat = ".zrus";
    [ObservableProperty] private bool _isDestinationPinned;

    [ObservableProperty] private IHierarchicalModel? _gridSource;
    [ObservableProperty] private StagedSourceItemViewModel? _selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedTotalStagedBytes))]
    [NotifyPropertyChangedFor(nameof(TotalBytes))]
    private long _totalStagedBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalFiles))]
    private int _totalFilesCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExcludedFiles))]
    private int _excludedFilesCount;

    public ObservableCollection<StagedSourceItemViewModel> StagedItems { get; } = [];
    public ObservableCollection<string> ExclusionPaths { get; } = [];
    public IReadOnlyCollection<string> ExcludedPaths => ExclusionPaths;

    public long TotalBytes => TotalStagedBytes;
    public int TotalFiles => TotalFilesCount;
    public int ExcludedFiles => ExcludedFilesCount;
    public string FormattedTotalStagedBytes => DataMetricsFormatter.FormatBytes(TotalStagedBytes);

    /// <summary>
    /// Every source staged for compression.
    /// </summary>
    public IReadOnlyList<string> SourcePaths
    {
        get
        {
            if (StagedItems.Count > 0)
            {
                return StagedItems.Select(i => i.FullPath).ToList();
            }
            return string.IsNullOrEmpty(SourcePath) ? [] : [SourcePath];
        }
    }

    public bool HasMultipleSources => SourcePaths.Count > 1;

    public string SourcePathsDisplay => string.Join(Environment.NewLine, SourcePaths);

    public IReadOnlyList<string> Formats => AvailableFormats;

    public Func<Task<IReadOnlyList<string>?>>? RequestSourceFiles { get; set; }
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

    public string? ActivePreset => CompressionLevel switch
    {
        3 => "Fast",
        9 => "Balanced",
        15 => "High",
        22 => "Ultra",
        _ => null
    };

    public bool IsFastSelected => CompressionLevel == 3;
    public bool IsBalancedSelected => CompressionLevel == 9;
    public bool IsHighSelected => CompressionLevel == 15;
    public bool IsUltraSelected => CompressionLevel == 22;
    public bool IsCustomSelected => !IsFastSelected && !IsBalancedSelected && !IsHighSelected && !IsUltraSelected;

    public string CurrentRatioEstimate => CompressionLevel switch
    {
        <= 5 => "~50%",
        <= 11 => "~65%",
        <= 18 => "~75%",
        _ => "~80%"
    };

    public string CurrentThroughputEstimate => CompressionLevel switch
    {
        <= 5 => "Fastest",
        <= 11 => "Balanced",
        <= 18 => "High Ratio",
        _ => "Maximum"
    };

    public CompressionSettingsViewModel()
    {
        RebuildGridSource();
    }

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

    private bool _isInternalSourcePathSync;

    partial void OnSourcePathChanged(string? oldValue, string newValue)
    {
        if (_isInternalSourcePathSync)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(newValue))
        {
            return;
        }

        if (StagedItems.Count == 0 || !string.Equals(StagedItems[0].FullPath, newValue, StringComparison.Ordinal))
        {
            StageSources([newValue]);
        }
    }

    private bool _isUpdatingDerivedDestination;

    partial void OnDestinationPathChanged(string? oldValue, string newValue)
    {
        if (_isUpdatingDerivedDestination)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(newValue))
        {
            IsDestinationPinned = false;
        }
        else
        {
            IsDestinationPinned = true;
        }
    }

    partial void OnSelectedFormatChanged(string? oldValue, string newValue)
    {
        if (!AvailableFormats.Contains(newValue, StringComparer.OrdinalIgnoreCase))
        {
            SelectedFormat = AvailableFormats[0];
            return;
        }

        if (string.IsNullOrEmpty(DestinationPath))
        {
            UpdateDerivedDestinationPath();
            return;
        }

        if (!string.IsNullOrEmpty(oldValue) && DestinationPath.EndsWith(oldValue, StringComparison.OrdinalIgnoreCase))
        {
            _isUpdatingDerivedDestination = true;
            try
            {
                DestinationPath = DestinationPath[..^oldValue.Length] + newValue;
            }
            finally
            {
                _isUpdatingDerivedDestination = false;
            }
        }
        else
        {
            foreach (var fmt in AvailableFormats)
            {
                if (DestinationPath.EndsWith(fmt, StringComparison.OrdinalIgnoreCase))
                {
                    _isUpdatingDerivedDestination = true;
                    try
                    {
                        DestinationPath = DestinationPath[..^fmt.Length] + newValue;
                    }
                    finally
                    {
                        _isUpdatingDerivedDestination = false;
                    }
                    break;
                }
            }
        }
    }

    public void StageSources(IReadOnlyList<string> paths)
    {
        UnhookAllItems();
        StagedItems.Clear();

        var staged = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var path in staged)
        {
            var item = StagedSourceItemViewModel.FromFileSystem(path);
            HookItem(item);
            StagedItems.Add(item);
        }

        _isInternalSourcePathSync = true;
        try
        {
            SourcePath = staged.Count > 0 ? staged[0] : string.Empty;
        }
        finally
        {
            _isInternalSourcePathSync = false;
        }

        RecalculateMetrics();
        UpdateDerivedDestinationPath();
        RebuildGridSource();
    }

    public void AddSources(IReadOnlyList<string> paths)
    {
        var staged = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var path in staged)
        {
            var fullPath = Path.GetFullPath(path);
            if (StagedItems.Any(i => string.Equals(i.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var item = StagedSourceItemViewModel.FromFileSystem(path);
            HookItem(item);
            StagedItems.Add(item);
        }

        if (StagedItems.Count > 0)
        {
            _isInternalSourcePathSync = true;
            try
            {
                SourcePath = StagedItems[0].FullPath;
            }
            finally
            {
                _isInternalSourcePathSync = false;
            }
        }

        RecalculateMetrics();
        UpdateDerivedDestinationPath();
        RebuildGridSource();
    }

    private void UpdateDerivedDestinationPath()
    {
        if (IsDestinationPinned)
        {
            return;
        }

        if (StagedItems.Count == 0)
        {
            if (string.IsNullOrEmpty(SourcePath))
            {
                _isUpdatingDerivedDestination = true;
                try
                {
                    DestinationPath = string.Empty;
                }
                finally
                {
                    _isUpdatingDerivedDestination = false;
                }
            }
            return;
        }

        string derived;
        if (StagedItems.Count == 1)
        {
            var itemPath = StagedItems[0].FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            derived = itemPath + SelectedFormat;
        }
        else
        {
            var firstPath = StagedItems[0].FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parentDir = Path.GetDirectoryName(firstPath);
            var archiveFileName = "Archive" + SelectedFormat;
            derived = !string.IsNullOrEmpty(parentDir) ? Path.Combine(parentDir, archiveFileName) : archiveFileName;
        }

        _isUpdatingDerivedDestination = true;
        try
        {
            DestinationPath = derived;
        }
        finally
        {
            _isUpdatingDerivedDestination = false;
        }
    }

    public void RecalculateMetrics()
    {
        long totalStagedBytes = 0;
        int totalFiles = 0;
        int excludedFiles = 0;
        var exclusions = new List<string>();

        void ProcessItem(StagedSourceItemViewModel item, bool parentExcluded)
        {
            bool isEffectivelyExcluded = parentExcluded || item.IsExcluded;

            if (item.IsExcluded)
            {
                if (!string.IsNullOrEmpty(item.FullPath) && !exclusions.Contains(item.FullPath, StringComparer.OrdinalIgnoreCase))
                {
                    exclusions.Add(item.FullPath);
                }
            }

            if (item.IsDirectory)
            {
                foreach (var child in item.Children)
                {
                    ProcessItem(child, isEffectivelyExcluded);
                }
            }
            else
            {
                totalFiles++;
                if (isEffectivelyExcluded)
                {
                    excludedFiles++;
                }
                else
                {
                    totalStagedBytes += item.Size;
                }
            }
        }

        foreach (var root in StagedItems)
        {
            ProcessItem(root, root.IsExcluded);
        }

        TotalStagedBytes = totalStagedBytes;
        TotalFilesCount = totalFiles;
        ExcludedFilesCount = excludedFiles;

        ExclusionPaths.Clear();
        foreach (var path in exclusions)
        {
            ExclusionPaths.Add(path);
        }

        OnPropertyChanged(nameof(FormattedTotalStagedBytes));
        OnPropertyChanged(nameof(SourcePaths));
        OnPropertyChanged(nameof(HasMultipleSources));
        OnPropertyChanged(nameof(SourcePathsDisplay));
    }

    private void HookItem(StagedSourceItemViewModel item)
    {
        item.PropertyChanged -= OnStagedItemPropertyChanged;
        item.PropertyChanged += OnStagedItemPropertyChanged;
        foreach (var child in item.Children)
        {
            HookItem(child);
        }
    }

    private void UnhookItem(StagedSourceItemViewModel item)
    {
        item.PropertyChanged -= OnStagedItemPropertyChanged;
        foreach (var child in item.Children)
        {
            UnhookItem(child);
        }
    }

    private void UnhookAllItems()
    {
        foreach (var item in StagedItems)
        {
            UnhookItem(item);
        }
    }

    private void OnStagedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StagedSourceItemViewModel.IsExcluded))
        {
            RecalculateMetrics();
        }
    }

    private void RebuildGridSource()
    {
        var model = new HierarchicalModel(new HierarchicalOptions
        {
            ChildrenPropertyPath = nameof(StagedSourceItemViewModel.Children),
            IsExpandedPropertyPath = nameof(StagedSourceItemViewModel.IsExpanded),
            VirtualizeChildren = true
        });
        model.SetRoots(StagedItems);
        GridSource = model;
    }

    [RelayCommand]
    public async Task AddFilesAsync(object? parameter = null)
    {
        if (parameter is IEnumerable<string> paths)
        {
            AddSources(paths.ToList());
            return;
        }

        if (parameter is string single)
        {
            AddSources([single]);
            return;
        }

        if (RequestSourceFiles != null)
        {
            var files = await RequestSourceFiles.Invoke();
            if (files != null && files.Count > 0)
            {
                AddSources(files);
            }
        }
        else if (RequestSourceFile != null)
        {
            var file = await RequestSourceFile.Invoke();
            if (!string.IsNullOrEmpty(file))
            {
                AddSources([file]);
            }
        }
    }

    [RelayCommand]
    public async Task AddFolderAsync(object? parameter = null)
    {
        if (parameter is string folderPath && !string.IsNullOrEmpty(folderPath))
        {
            AddSources([folderPath]);
            return;
        }

        if (RequestSourceFolder != null)
        {
            var path = await RequestSourceFolder.Invoke();
            if (!string.IsNullOrEmpty(path))
            {
                AddSources([path]);
            }
        }
    }

    [RelayCommand]
    public void RemoveSelected(StagedSourceItemViewModel? item = null)
    {
        var target = item ?? SelectedItem;
        if (target == null)
        {
            return;
        }

        if (target.Parent == null)
        {
            // Root item un-stages
            UnhookItem(target);
            StagedItems.Remove(target);
            if (ReferenceEquals(SelectedItem, target))
            {
                SelectedItem = null;
            }

            _isInternalSourcePathSync = true;
            try
            {
                SourcePath = StagedItems.Count > 0 ? StagedItems[0].FullPath : string.Empty;
            }
            finally
            {
                _isInternalSourcePathSync = false;
            }

            RecalculateMetrics();
            UpdateDerivedDestinationPath();
            RebuildGridSource();
        }
        else
        {
            // Child item is marked excluded
            target.SetExcluded(true);
            RecalculateMetrics();
        }
    }

    [RelayCommand]
    public void ClearAll()
    {
        UnhookAllItems();
        StagedItems.Clear();
        ExclusionPaths.Clear();
        SelectedItem = null;

        _isInternalSourcePathSync = true;
        try
        {
            SourcePath = string.Empty;
        }
        finally
        {
            _isInternalSourcePathSync = false;
        }

        RecalculateMetrics();

        if (!IsDestinationPinned)
        {
            _isUpdatingDerivedDestination = true;
            try
            {
                DestinationPath = string.Empty;
            }
            finally
            {
                _isUpdatingDerivedDestination = false;
            }
        }

        RebuildGridSource();
    }

    [RelayCommand]
    public void ToggleExclusion(StagedSourceItemViewModel? item = null)
    {
        var target = item ?? SelectedItem;
        if (target == null)
        {
            return;
        }

        target.SetExcluded(!target.IsExcluded);
        RecalculateMetrics();
    }

    [RelayCommand]
    public void ExpandAll()
    {
        SetExpandedRecursive(StagedItems, true);
    }

    [RelayCommand]
    public void CollapseAll()
    {
        SetExpandedRecursive(StagedItems, false);
    }

    private static void SetExpandedRecursive(IEnumerable<StagedSourceItemViewModel> items, bool isExpanded)
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

    [RelayCommand]
    public void SetPreset(int level)
    {
        CompressionLevel = Math.Clamp(level, 1, 22);
    }

    [RelayCommand]
    public void SelectPreset(object? parameter)
    {
        if (parameter is int level)
        {
            SetPreset(level);
        }
        else if (parameter is string name)
        {
            SelectPresetByName(name);
        }
        else if (parameter is CompressionPreset preset)
        {
            SetPreset(preset.Level);
        }
    }

    public void SelectPreset(int level) => SetPreset(level);

    public void SelectPreset(string name) => SelectPresetByName(name);

    private void SelectPresetByName(string name)
    {
        if (string.Equals(name, "Fast", StringComparison.OrdinalIgnoreCase))
        {
            SetPreset(3);
        }
        else if (string.Equals(name, "Balanced", StringComparison.OrdinalIgnoreCase))
        {
            SetPreset(9);
        }
        else if (string.Equals(name, "High", StringComparison.OrdinalIgnoreCase))
        {
            SetPreset(15);
        }
        else if (string.Equals(name, "Ultra", StringComparison.OrdinalIgnoreCase))
        {
            SetPreset(22);
        }
    }

    [RelayCommand]
    public async Task BrowseSourceFileAsync()
    {
        if (RequestSourceFile != null)
        {
            var path = await RequestSourceFile.Invoke();
            if (!string.IsNullOrEmpty(path))
            {
                StageSources([path]);
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
                StageSources([path]);
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

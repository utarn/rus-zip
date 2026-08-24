using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using RusZip.Desktop.Models;
using RusZip.Desktop.Services;

namespace RusZip.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IArchiveEngine _engine;
    private readonly IFileAssociationService _associationService;
    private readonly IRecentArchivesService _recentArchivesService;
    private readonly IArchivePreviewService _previewService;

    public static IReadOnlyCollection<string> SupportedExtensions => ArchiveFormatRegistry.SupportedExtensions;

    public static bool IsSupportedArchive(string? path) => ArchiveFormatRegistry.IsSupportedArchive(path);

    [ObservableProperty] private ArchiveBrowserViewModel _browser;
    [ObservableProperty] private CompressionSettingsViewModel _settings;
    [ObservableProperty] private SettingsViewModel _settingsViewModel;
    [ObservableProperty] private OperationProgressViewModel _progress;
    [ObservableProperty] private bool _hasOpenArchive;
    [ObservableProperty] private bool _isCompressDialogVisible;
    [ObservableProperty] private bool _isSettingsDialogVisible;
    [ObservableProperty] private bool _isDragOver;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private ThemeMode _currentTheme = ThemeMode.System;

    public ObservableCollection<RecentArchiveItemViewModel> RecentArchives { get; } = [];
    public bool HasRecentArchives => RecentArchives.Count > 0;
    public IRecentArchivesService RecentArchivesService => _recentArchivesService;
    public IArchivePreviewService PreviewService => _previewService;

    public bool IsDarkTheme => CurrentTheme == ThemeMode.Dark;
    public bool IsLightTheme => CurrentTheme == ThemeMode.Light;
    public bool IsSystemTheme => CurrentTheme == ThemeMode.System;

    public string ThemeIconKey => CurrentTheme switch
    {
        ThemeMode.Dark => "Icon.ThemeDark",
        ThemeMode.Light => "Icon.ThemeLight",
        _ => "Icon.ThemeLight"
    };

    public string ThemeDisplayName => CurrentTheme switch
    {
        ThemeMode.Dark => "Dark",
        ThemeMode.Light => "Light",
        _ => "System"
    };

    partial void OnCurrentThemeChanged(ThemeMode value)
    {
        App.SetTheme(value);
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(ThemeIconKey));
        OnPropertyChanged(nameof(ThemeDisplayName));
    }

    [RelayCommand]
    public void ToggleTheme()
    {
        CurrentTheme = CurrentTheme switch
        {
            ThemeMode.System => ThemeMode.Dark,
            ThemeMode.Dark => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.System,
            _ => ThemeMode.System
        };
    }

    [RelayCommand]
    public async Task OpenSettingsAsync()
    {
        await SettingsViewModel.LoadAssociationsAsync();
        IsSettingsDialogVisible = true;
    }

    [RelayCommand]
    public void CloseSettingsDialog()
    {
        IsSettingsDialogVisible = false;
    }

    public Func<Task<string?>>? RequestExtractDestinationFolder { get; set; }
    public Func<Task<string?>>? RequestOpenArchivePicker { get; set; }
    public Func<Task<IReadOnlyList<string>?>>? RequestAppendSourcePaths { get; set; }
    public Func<int, IReadOnlyList<string>, Task<bool>>? ConfirmDeleteAsync { get; set; }
    public Func<ArchiveTestResult, Task>? RequestShowTestResultDialog { get; set; }
    public Func<ArchivePropertiesViewModel, Task>? RequestShowPropertiesDialog { get; set; }

    public bool CanAppendToArchive => HasOpenArchive && Browser.CanCompress;
    public bool CanDeleteFromArchive => HasOpenArchive && Browser.CanCompress && (Browser.SelectedItem != null || Browser.SelectedItems.Count > 0);

    /// <summary>
    /// Formats a status message for the one-line status bar: strips C0/C1 control bytes
    /// (including ESC/NUL from attacker-controlled entry names) and collapses newlines.
    /// </summary>
    private static string FormatStatus(string message) => EntryNameSanitizer.SingleLine(message);

    public MainWindowViewModel(IArchiveEngine engine)
        : this(engine, FileAssociationServiceFactory.CreateDefault(), new JsonRecentArchivesService(), new ArchivePreviewService(engine))
    {
    }

    public MainWindowViewModel(IArchiveEngine engine, IFileAssociationService associationService)
        : this(engine, associationService, new JsonRecentArchivesService(), new ArchivePreviewService(engine))
    {
    }

    public MainWindowViewModel(IArchiveEngine engine, IFileAssociationService associationService, IRecentArchivesService recentArchivesService)
        : this(engine, associationService, recentArchivesService, new ArchivePreviewService(engine))
    {
    }

    public MainWindowViewModel(
        IArchiveEngine engine,
        IFileAssociationService associationService,
        IRecentArchivesService recentArchivesService,
        IArchivePreviewService previewService)
    {
        _engine = engine;
        _associationService = associationService;
        _recentArchivesService = recentArchivesService;
        _previewService = previewService;
        _settingsViewModel = new SettingsViewModel(associationService);
        _browser = new ArchiveBrowserViewModel();
        _settings = new CompressionSettingsViewModel();
        _progress = new OperationProgressViewModel();

        WireBrowser(_browser);

        _recentArchivesService.RecentPathsChanged += (_, _) => SyncRecentArchives();
        SyncRecentArchives();
    }

    public async Task InitializeRecentArchivesAsync()
    {
        await _recentArchivesService.LoadAsync();
        SyncRecentArchives();
    }

    public void SyncRecentArchives()
    {
        RecentArchives.Clear();
        foreach (var path in _recentArchivesService.RecentPaths)
        {
            RecentArchives.Add(new RecentArchiveItemViewModel(path, OpenRecentArchiveCommand, RemoveRecentArchiveCommand));
        }
        OnPropertyChanged(nameof(HasRecentArchives));
    }

    private void WireBrowser(ArchiveBrowserViewModel browser)
    {
        browser.ExtractRequested += OnBrowserExtractRequestedAsync;
        browser.ExtractItemRequested += OnBrowserExtractItemRequestedAsync;
        browser.ExtractItemsRequested += OnBrowserExtractItemsRequestedAsync;
        browser.AppendRequested += OnBrowserAppendRequestedAsync;
        browser.DeleteRequested += OnBrowserDeleteRequestedAsync;
        browser.PreviewItemRequested += OnBrowserPreviewItemRequestedAsync;
        browser.PropertiesRequested += OnBrowserPropertiesRequestedAsync;
        browser.PropertyChanged += OnBrowserPropertyChanged;
        browser.SelectedItems.CollectionChanged += OnBrowserSelectedItemsCollectionChanged;
        if (ConfirmDeleteAsync != null)
        {
            browser.ConfirmDeleteAsync = ConfirmDeleteAsync;
        }
    }

    private void UnwireBrowser(ArchiveBrowserViewModel browser)
    {
        browser.ExtractRequested -= OnBrowserExtractRequestedAsync;
        browser.ExtractItemRequested -= OnBrowserExtractItemRequestedAsync;
        browser.ExtractItemsRequested -= OnBrowserExtractItemsRequestedAsync;
        browser.AppendRequested -= OnBrowserAppendRequestedAsync;
        browser.DeleteRequested -= OnBrowserDeleteRequestedAsync;
        browser.PreviewItemRequested -= OnBrowserPreviewItemRequestedAsync;
        browser.PropertiesRequested -= OnBrowserPropertiesRequestedAsync;
        browser.PropertyChanged -= OnBrowserPropertyChanged;
        browser.SelectedItems.CollectionChanged -= OnBrowserSelectedItemsCollectionChanged;
    }

    private async Task OnBrowserPropertiesRequestedAsync(ArchiveItemViewModel? item)
    {
        await ShowPropertiesAsync(item);
    }

    private async Task OnBrowserPreviewItemRequestedAsync(ArchiveItemViewModel item)
    {
        if (string.IsNullOrEmpty(Browser.LoadedArchivePath))
            return;

        try
        {
            StatusText = FormatStatus($"Previewing {item.Name}...");
            await _previewService.PreviewEntryAsync(Browser.LoadedArchivePath, item.RelativePath);
        }
        catch (Exception ex)
        {
            StatusText = FormatStatus($"Preview failed: {ex.Message}");
        }
    }

    private void OnBrowserSelectedItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnBrowserSelectionChanged();
    }

    private void OnBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ArchiveBrowserViewModel.CanCompress)
            or nameof(ArchiveBrowserViewModel.SelectedItem))
        {
            OnBrowserSelectionChanged();
        }
    }

    private void OnBrowserSelectionChanged()
    {
        OnPropertyChanged(nameof(CanAppendToArchive));
        OnPropertyChanged(nameof(CanDeleteFromArchive));
        AppendFilesCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        ExtractSelectedItemCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasOpenArchiveChanged(bool value)
    {
        OnBrowserSelectionChanged();
    }

    private async Task OnBrowserExtractRequestedAsync()
    {
        if (RequestExtractDestinationFolder != null)
        {
            var destination = await RequestExtractDestinationFolder.Invoke();
            if (!string.IsNullOrEmpty(destination))
            {
                await ExecuteExtractAllAsync(destination);
            }
        }
    }

    private async Task OnBrowserExtractItemRequestedAsync(ArchiveItemViewModel item)
    {
        if (RequestExtractDestinationFolder != null)
        {
            var destination = await RequestExtractDestinationFolder.Invoke();
            if (!string.IsNullOrEmpty(destination))
            {
                await ExecuteExtractItemAsync(item, destination);
            }
        }
    }

    private async Task OnBrowserExtractItemsRequestedAsync(IReadOnlyList<ArchiveItemViewModel> items)
    {
        if (RequestExtractDestinationFolder != null)
        {
            var destination = await RequestExtractDestinationFolder.Invoke();
            if (!string.IsNullOrEmpty(destination))
            {
                await ExecuteExtractItemsAsync(items, destination);
            }
        }
    }

    private async Task OnBrowserAppendRequestedAsync()
    {
        await AppendFilesAsync();
    }

    private async Task OnBrowserDeleteRequestedAsync(IReadOnlyList<ArchiveItemViewModel> items)
    {
        var paths = items.Select(i => i.RelativePath).Distinct().ToList();
        await ExecuteDeleteEntriesAsync(paths);
    }

    [RelayCommand]
    public void CloseArchive()
    {
        _ = _previewService.CleanupAsync();
        HasOpenArchive = false;
        UnwireBrowser(Browser);
        Browser = new ArchiveBrowserViewModel();
        WireBrowser(Browser);
        StatusText = "Ready";
        OnBrowserSelectionChanged();
    }

    [RelayCommand]
    public async Task OpenArchiveAsync(string archivePath)
    {
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) return;

        try
        {
            StatusText = FormatStatus($"Opening {Path.GetFileName(archivePath)}...");
            var entries = await _engine.ListEntriesAsync(archivePath);
            Browser.LoadEntries(archivePath, entries);
            HasOpenArchive = true;
            IsCompressDialogVisible = false;
            StatusText = FormatStatus($"Loaded {entries.Count} entries from {Path.GetFileName(archivePath)}");
            OnBrowserSelectionChanged();
            await _recentArchivesService.AddRecentPathAsync(archivePath);
        }
        catch (Exception ex)
        {
            StatusText = FormatStatus($"Failed to open archive: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task OpenRecentArchiveAsync(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;

        if (!File.Exists(path))
        {
            StatusText = FormatStatus($"Recent archive not found: {Path.GetFileName(path)}. Removed from recent history.");
            await _recentArchivesService.RemoveRecentPathAsync(path);
            return;
        }

        await OpenArchiveAsync(path);
    }

    [RelayCommand]
    public async Task RemoveRecentArchiveAsync(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        await _recentArchivesService.RemoveRecentPathAsync(path);
    }

    [RelayCommand]
    public async Task ClearRecentArchivesAsync()
    {
        await _recentArchivesService.ClearRecentPathsAsync();
        StatusText = FormatStatus("Recent archives history cleared.");
    }

    public async Task RefreshArchiveAsync()
    {
        if (string.IsNullOrEmpty(Browser.LoadedArchivePath) || !File.Exists(Browser.LoadedArchivePath))
            return;

        var archivePath = Browser.LoadedArchivePath;
        var entries = await _engine.ListEntriesAsync(archivePath);
        Browser.LoadEntries(archivePath, entries);
        OnBrowserSelectionChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAppendToArchive))]
    public async Task AppendFilesAsync()
    {
        if (!HasOpenArchive || !Browser.CanCompress)
            return;

        if (RequestAppendSourcePaths != null)
        {
            var paths = await RequestAppendSourcePaths.Invoke();
            if (paths != null && paths.Count > 0)
            {
                await ExecuteAppendAsync(paths);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteFromArchive))]
    public async Task DeleteSelectedAsync()
    {
        if (!HasOpenArchive || !Browser.CanCompress)
            return;

        var items = Browser.GetEffectiveSelectedItems();
        if (items.Count == 0)
            return;

        var paths = items.Select(i => i.RelativePath).Distinct().ToList();

        var confirm = ConfirmDeleteAsync ?? Browser.ConfirmDeleteAsync;
        if (confirm != null)
        {
            var confirmed = await confirm(items.Count, paths);
            if (!confirmed)
                return;
        }

        await ExecuteDeleteEntriesAsync(paths);
    }

    [RelayCommand]
    public async Task OpenArchivePickerAsync()
    {
        if (RequestOpenArchivePicker != null)
        {
            var path = await RequestOpenArchivePicker.Invoke();
            if (!string.IsNullOrEmpty(path))
            {
                await OpenArchiveAsync(path);
            }
        }
    }

    [RelayCommand]
    public void CreateArchive()
    {
        ShowCompressDialog();
    }

    [RelayCommand]
    public void ShowCompressDialog(string? initialSourcePath = null)
    {
        if (!string.IsNullOrEmpty(initialSourcePath))
        {
            Settings.StageSources([initialSourcePath]);
        }

        IsCompressDialogVisible = true;
    }

    [RelayCommand]
    public void CloseCompressDialog()
    {
        IsCompressDialogVisible = false;
    }

    [RelayCommand]
    public async Task ExecuteCompressAsync()
    {
        var sourcePaths = Settings.SourcePaths.Count > 0
            ? Settings.SourcePaths
            : (!string.IsNullOrEmpty(Settings.SourcePath) ? [Settings.SourcePath] : Array.Empty<string>());

        if (sourcePaths.Count == 0 || string.IsNullOrEmpty(Settings.DestinationPath))
            return;

        var cts = Progress.CreateCancellationTokenSource();
        Progress.OperationTitle = "Compressing Archive...";
        var progressHandler = new Progress<ProgressReport>(Progress.ReportProgress);

        bool success = false;
        try
        {
            var req = new ArchiveCompressionRequest(
                sourcePaths,
                Settings.DestinationPath,
                Settings.CompressionLevel,
                BaseDirectory: null,
                ExcludedPaths: Settings.ExcludedPaths
            );

            await Task.Run(async () => await _engine.CompressAsync(req, progressHandler, cts.Token), cts.Token);
            success = true;
            IsCompressDialogVisible = false;
            await OpenArchiveAsync(Settings.DestinationPath);
        }
        catch (OperationCanceledException)
        {
            StatusText = FormatStatus("Compression cancelled.");
            if (File.Exists(Settings.DestinationPath))
            {
                try { File.Delete(Settings.DestinationPath); } catch { /* Ignore */ }
            }
        }
        catch (Exception ex)
        {
            StatusText = FormatStatus($"Compression failed: {ex.Message}");
        }
        finally
        {
            await Progress.FinishOperationAsync(success, StatusText);
        }
    }

    [RelayCommand]
    public async Task ExecuteExtractAllAsync(string destinationDirectory)
    {
        if (!HasOpenArchive || string.IsNullOrEmpty(Browser.LoadedArchivePath))
            return;

        var cts = Progress.CreateCancellationTokenSource();
        Progress.OperationTitle = "Extracting Archive...";
        var progressHandler = new Progress<ProgressReport>(Progress.ReportProgress);

        bool success = false;
        try
        {
            var req = new ArchiveExtractionRequest(
                Browser.LoadedArchivePath,
                destinationDirectory,
                Overwrite: true,
                Limits: Browser.ExtractionSettings.BuildLimits());
            await Task.Run(async () => await _engine.ExtractAsync(req, progressHandler, cts.Token), cts.Token);
            success = true;
            StatusText = FormatStatus($"Extracted to {destinationDirectory}");
        }
        catch (OperationCanceledException)
        {
            StatusText = FormatStatus("Extraction cancelled.");
        }
        catch (Exception ex)
        {
            StatusText = FormatStatus($"Extraction failed: {ex.Message}");
        }
        finally
        {
            await Progress.FinishOperationAsync(success, StatusText);
        }
    }

    public async Task ExecuteExtractItemAsync(ArchiveItemViewModel item, string destinationDirectory)
    {
        if (!HasOpenArchive || string.IsNullOrEmpty(Browser.LoadedArchivePath))
            return;

        var cts = Progress.CreateCancellationTokenSource();
        Progress.OperationTitle = $"Extracting {item.Name}...";
        var progressHandler = new Progress<ProgressReport>(Progress.ReportProgress);

        bool success = false;
        try
        {
            // Selective extraction (F-14 / PRD #23 story 7): pass the selected node's relative path
            // as the entry filter. For a file node this is an exact-path match; for a folder node the
            // directory-prefix match extracts the folder's entire subtree. Progress totals reflect
            // only the filtered subset.
            var req = new ArchiveExtractionRequest(
                Browser.LoadedArchivePath,
                destinationDirectory,
                Overwrite: true,
                Limits: Browser.ExtractionSettings.BuildLimits(),
                Entries: [item.RelativePath]);
            await Task.Run(async () => await _engine.ExtractAsync(req, progressHandler, cts.Token), cts.Token);
            success = true;
            StatusText = FormatStatus($"Extracted {item.Name} to {destinationDirectory}");
        }
        catch (OperationCanceledException)
        {
            StatusText = FormatStatus("Extraction cancelled.");
        }
        catch (Exception ex)
        {
            StatusText = FormatStatus($"Extraction failed: {ex.Message}");
        }
        finally
        {
            await Progress.FinishOperationAsync(success, StatusText);
        }
    }

    public async Task ExecuteExtractItemsAsync(IReadOnlyList<ArchiveItemViewModel> items, string destinationDirectory)
    {
        if (!HasOpenArchive || string.IsNullOrEmpty(Browser.LoadedArchivePath) || items == null || items.Count == 0)
            return;

        var cts = Progress.CreateCancellationTokenSource();
        Progress.OperationTitle = $"Extracting {items.Count} items...";
        var progressHandler = new Progress<ProgressReport>(Progress.ReportProgress);

        bool success = false;
        try
        {
            var entryPaths = items.Select(i => i.RelativePath).Distinct().ToList();
            var req = new ArchiveExtractionRequest(
                Browser.LoadedArchivePath,
                destinationDirectory,
                Overwrite: true,
                Limits: Browser.ExtractionSettings.BuildLimits(),
                Entries: entryPaths);
            await Task.Run(async () => await _engine.ExtractAsync(req, progressHandler, cts.Token), cts.Token);
            success = true;
            StatusText = FormatStatus($"Extracted {items.Count} item{(items.Count == 1 ? "" : "s")} to {destinationDirectory}");
        }
        catch (OperationCanceledException)
        {
            StatusText = FormatStatus("Extraction cancelled.");
        }
        catch (Exception ex)
        {
            StatusText = FormatStatus($"Extraction failed: {ex.Message}");
        }
        finally
        {
            await Progress.FinishOperationAsync(success, StatusText);
        }
    }

    public async Task ExecuteAppendAsync(IReadOnlyList<string> sourcePaths)
    {
        if (!HasOpenArchive || string.IsNullOrEmpty(Browser.LoadedArchivePath) || sourcePaths == null || sourcePaths.Count == 0)
            return;

        var archivePath = Browser.LoadedArchivePath;
        var descriptor = ArchiveFormatRegistry.Detect(archivePath);
        if (!descriptor.CanCompress || descriptor.Format == ArchiveFormat.Zst)
        {
            StatusText = FormatStatus($"Appending to {descriptor.Format} archives is not supported.");
            return;
        }

        var cts = Progress.CreateCancellationTokenSource();
        Progress.OperationTitle = "Adding to Archive...";
        var progressHandler = new Progress<ProgressReport>(Progress.ReportProgress);

        bool success = false;
        try
        {
            var req = new ArchiveAppendRequest(
                archivePath,
                sourcePaths,
                CompressionLevel: descriptor.DefaultCompressionLevel);

            var result = await Task.Run(async () => await _engine.AppendAsync(req, progressHandler, cts.Token), cts.Token);
            success = true;
            StatusText = FormatStatus($"Added {result.AddedFiles} file{(result.AddedFiles == 1 ? "" : "s")} to {Path.GetFileName(archivePath)}");
            await RefreshArchiveAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText = FormatStatus("Append cancelled.");
        }
        catch (Exception ex)
        {
            StatusText = FormatStatus($"Append failed: {ex.Message}");
        }
        finally
        {
            await Progress.FinishOperationAsync(success, StatusText);
        }
    }

    public async Task ExecuteDeleteEntriesAsync(IReadOnlyList<string> entryPaths)
    {
        if (!HasOpenArchive || string.IsNullOrEmpty(Browser.LoadedArchivePath) || entryPaths == null || entryPaths.Count == 0)
            return;

        var archivePath = Browser.LoadedArchivePath;
        var descriptor = ArchiveFormatRegistry.Detect(archivePath);
        if (!descriptor.CanCompress || descriptor.Format == ArchiveFormat.Zst)
        {
            StatusText = FormatStatus($"Deleting entries from {descriptor.Format} archives is not supported.");
            return;
        }

        var cts = Progress.CreateCancellationTokenSource();
        Progress.OperationTitle = "Deleting Entries...";
        var progressHandler = new Progress<ProgressReport>(Progress.ReportProgress);

        bool success = false;
        try
        {
            var req = new ArchiveDeleteRequest(
                archivePath,
                entryPaths,
                CompressionLevel: descriptor.DefaultCompressionLevel);

            var result = await Task.Run(async () => await _engine.DeleteEntriesAsync(req, progressHandler, cts.Token), cts.Token);
            success = true;
            StatusText = FormatStatus($"Deleted {result.DeletedEntriesCount} entr{(result.DeletedEntriesCount == 1 ? "y" : "ies")} from {Path.GetFileName(archivePath)}");
            await RefreshArchiveAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText = FormatStatus("Deletion cancelled.");
        }
        catch (Exception ex)
        {
            StatusText = FormatStatus($"Deletion failed: {ex.Message}");
        }
        finally
        {
            await Progress.FinishOperationAsync(success, StatusText);
        }
    }

    [RelayCommand]
    public async Task ExtractSelectedItemAsync()
    {
        if (Browser.SelectedItem != null)
        {
            await Browser.ExtractSelectedItemCommand.ExecuteAsync(null);
        }
        else if (HasOpenArchive)
        {
            await Browser.RequestExtractCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    public async Task CopyPathAsync(object? parameter = null)
    {
        await Browser.CopyPathCommand.ExecuteAsync(parameter);
    }

    [RelayCommand]
    public void SelectAll()
    {
        if (HasOpenArchive)
        {
            Browser.SelectAll();
        }
    }

    [RelayCommand]
    public void InvertSelection()
    {
        if (HasOpenArchive)
        {
            Browser.InvertSelection();
        }
    }

    [RelayCommand]
    public async Task CopyRelativePathAsync()
    {
        if (HasOpenArchive)
        {
            await Browser.CopySelectedItemPathAsync();
        }
    }

    [RelayCommand]
    public void ExpandAll()
    {
        if (HasOpenArchive)
        {
            Browser.ExpandAll();
        }
    }

    [RelayCommand]
    public void CollapseAll()
    {
        if (HasOpenArchive)
        {
            Browser.CollapseAll();
        }
    }

    public Action? RequestFocusFilter { get; set; }

    [RelayCommand]
    public void FocusFilter()
    {
        RequestFocusFilter?.Invoke();
    }

    public Action? RequestExit { get; set; }

    [RelayCommand]
    public void ExitApplication()
    {
        _ = _previewService.CleanupAsync();
        RequestExit?.Invoke();
    }

    [RelayCommand]
    public async Task ExtractAllMenuAsync()
    {
        if (!HasOpenArchive) return;
        if (RequestExtractDestinationFolder != null)
        {
            var destination = await RequestExtractDestinationFolder.Invoke();
            if (!string.IsNullOrEmpty(destination))
            {
                await ExecuteExtractAllAsync(destination);
            }
        }
    }

    [RelayCommand]
    public async Task RefreshArchive()
    {
        await RefreshArchiveAsync();
    }

    [RelayCommand]
    public async Task TestArchiveAsync()
    {
        if (!HasOpenArchive || string.IsNullOrEmpty(Browser.LoadedArchivePath))
            return;

        var archivePath = Browser.LoadedArchivePath;
        var fileName = Path.GetFileName(archivePath);

        var cts = Progress.CreateCancellationTokenSource();
        Progress.OperationTitle = $"Testing {fileName}...";
        var progressHandler = new Progress<ProgressReport>(Progress.ReportProgress);

        bool success = false;
        try
        {
            StatusText = FormatStatus($"Testing archive integrity for {fileName}...");
            var result = await Task.Run(async () => await _engine.TestArchiveAsync(archivePath, progressHandler, cts.Token), cts.Token);
            success = result.IsSuccess;
            StatusText = FormatStatus(result.IsSuccess
                ? $"Archive test passed: {result.TotalEntries} entries verified."
                : $"Archive test failed: {result.Errors.Count} error(s) found.");

            if (RequestShowTestResultDialog != null)
            {
                await RequestShowTestResultDialog.Invoke(result);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = FormatStatus("Archive testing cancelled.");
        }
        catch (Exception ex)
        {
            StatusText = FormatStatus($"Archive testing failed: {ex.Message}");
        }
        finally
        {
            await Progress.FinishOperationAsync(success, StatusText);
        }
    }

    [RelayCommand]
    public async Task ShowPropertiesAsync(ArchiveItemViewModel? specificItem = null)
    {
        if (!HasOpenArchive || string.IsNullOrEmpty(Browser.LoadedArchivePath))
            return;

        try
        {
            var targetItem = specificItem ?? Browser.SelectedItem;
            var propertiesVm = await ArchivePropertiesViewModel.CreateAsync(Browser.LoadedArchivePath, _engine, targetItem);
            StatusText = FormatStatus($"Properties loaded for {Path.GetFileName(Browser.LoadedArchivePath)}");
            if (RequestShowPropertiesDialog != null)
            {
                await RequestShowPropertiesDialog.Invoke(propertiesVm);
            }
        }
        catch (Exception ex)
        {
            StatusText = FormatStatus($"Failed to load properties: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task OpenFileAssociationsAsync()
    {
        await OpenSettingsAsync();
    }

    [RelayCommand]
    public void OpenDocumentation()
    {
        StatusText = FormatStatus("Documentation: https://gitlab.com/utarn/rus-zip-desktop");
    }

    [RelayCommand]
    public void OpenSupportedFormats()
    {
        StatusText = FormatStatus("Supported formats: .zrus, .zip, .rar, .7z, .zst, .gz, .tar.gz, .tar.zstd");
    }

    [RelayCommand]
    public void ShowAbout()
    {
        StatusText = FormatStatus("rus-zip - Modern High-Performance Compression Suite v1.0");
    }

    public async Task HandleDroppedPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;

        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (existing.Count == 0) return;

        var archives = existing.Where(p => File.Exists(p) && IsSupportedArchive(p)).ToList();
        var nonArchives = existing.Where(p => !(File.Exists(p) && IsSupportedArchive(p))).ToList();

        // Any non-archive file or folder is staged for the compression wizard. Multiple sources
        // are all staged (F-27) and shown in the wizard's multi-source display; archive items in
        // the same drop are reported as ignored rather than silently discarded.
        if (nonArchives.Count > 0)
        {
            if (IsCompressDialogVisible)
            {
                Settings.AddSources(nonArchives);
            }
            else
            {
                Settings.StageSources(nonArchives);
                ShowCompressDialog();
            }

            if (archives.Count > 0)
            {
                StatusText = FormatStatus($"{archives.Count} archive{(archives.Count == 1 ? "" : "s")} ignored.");
            }
            return;
        }

        // All dropped items are archives: open the first and report the rest ignored.
        await OpenArchiveAsync(archives[0]);
        if (archives.Count > 1)
        {
            var ignored = archives.Count - 1;
            StatusText = FormatStatus($"Opened {Path.GetFileName(archives[0])}; {ignored} other archive{(ignored == 1 ? "" : "s")} ignored.");
        }
    }
}

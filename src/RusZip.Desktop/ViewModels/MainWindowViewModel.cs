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

    /// <summary>
    /// Formats a status message for the one-line status bar: strips C0/C1 control bytes
    /// (including ESC/NUL from attacker-controlled entry names) and collapses newlines.
    /// </summary>
    private static string FormatStatus(string message) => EntryNameSanitizer.SingleLine(message);

    public MainWindowViewModel(IArchiveEngine engine)
        : this(engine, FileAssociationServiceFactory.CreateDefault())
    {
    }

    public MainWindowViewModel(IArchiveEngine engine, IFileAssociationService associationService)
    {
        _engine = engine;
        _associationService = associationService;
        _settingsViewModel = new SettingsViewModel(associationService);
        _browser = new ArchiveBrowserViewModel();
        _settings = new CompressionSettingsViewModel();
        _progress = new OperationProgressViewModel();

        _browser.ExtractRequested += OnBrowserExtractRequestedAsync;
        _browser.ExtractItemRequested += OnBrowserExtractItemRequestedAsync;
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

    [RelayCommand]
    public void CloseArchive()
    {
        HasOpenArchive = false;
        Browser = new ArchiveBrowserViewModel();
        Browser.ExtractRequested += OnBrowserExtractRequestedAsync;
        Browser.ExtractItemRequested += OnBrowserExtractItemRequestedAsync;
        StatusText = "Ready";
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
        }
        catch (Exception ex)
        {
            StatusText = FormatStatus($"Failed to open archive: {ex.Message}");
        }
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

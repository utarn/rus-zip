using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using RusZip.Desktop.Models;

namespace RusZip.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IArchiveEngine _engine;

    public static IReadOnlyCollection<string> SupportedExtensions => ArchiveFormatRegistry.SupportedExtensions;

    public static bool IsSupportedArchive(string? path) => ArchiveFormatRegistry.IsSupportedArchive(path);

    [ObservableProperty] private ArchiveBrowserViewModel _browser;
    [ObservableProperty] private CompressionSettingsViewModel _settings;
    [ObservableProperty] private OperationProgressViewModel _progress;
    [ObservableProperty] private bool _hasOpenArchive;
    [ObservableProperty] private bool _isCompressDialogVisible;
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

    public Func<Task<string?>>? RequestExtractDestinationFolder { get; set; }
    public Func<Task<string?>>? RequestOpenArchivePicker { get; set; }

    public MainWindowViewModel(IArchiveEngine engine)
    {
        _engine = engine;
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
            StatusText = $"Opening {Path.GetFileName(archivePath)}...";
            var entries = await _engine.ListEntriesAsync(archivePath);
            Browser.LoadEntries(archivePath, entries);
            HasOpenArchive = true;
            IsCompressDialogVisible = false;
            StatusText = $"Loaded {entries.Count} entries from {Path.GetFileName(archivePath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to open archive: {ex.Message}";
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
            Settings.SourcePath = initialSourcePath;
            Settings.DestinationPath = initialSourcePath + Settings.SelectedFormat;
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
        if (string.IsNullOrEmpty(Settings.SourcePath) || string.IsNullOrEmpty(Settings.DestinationPath))
            return;

        var cts = Progress.CreateCancellationTokenSource();
        Progress.OperationTitle = "Compressing Archive...";
        var progressHandler = new Progress<ProgressReport>(Progress.ReportProgress);

        bool success = false;
        try
        {
            var req = new ArchiveCompressionRequest(
                Settings.SourcePath,
                Settings.DestinationPath,
                Settings.CompressionLevel
            );

            await Task.Run(async () => await _engine.CompressAsync(req, progressHandler, cts.Token), cts.Token);
            success = true;
            IsCompressDialogVisible = false;
            await OpenArchiveAsync(Settings.DestinationPath);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Compression cancelled.";
            if (File.Exists(Settings.DestinationPath))
            {
                try { File.Delete(Settings.DestinationPath); } catch { /* Ignore */ }
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Compression failed: {ex.Message}";
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
            StatusText = $"Extracted to {destinationDirectory}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Extraction cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Extraction failed: {ex.Message}";
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
            var req = new ArchiveExtractionRequest(
                Browser.LoadedArchivePath,
                destinationDirectory,
                Overwrite: true,
                Limits: Browser.ExtractionSettings.BuildLimits());
            await Task.Run(async () => await _engine.ExtractAsync(req, progressHandler, cts.Token), cts.Token);
            success = true;
            StatusText = $"Extracted {item.Name} to {destinationDirectory}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Extraction cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Extraction failed: {ex.Message}";
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

        if (paths.Count == 1 && File.Exists(paths[0]) && IsSupportedArchive(paths[0]))
        {
            await OpenArchiveAsync(paths[0]);
        }
        else
        {
            ShowCompressDialog(paths[0]);
        }
    }
}

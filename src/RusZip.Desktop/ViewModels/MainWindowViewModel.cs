using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;

namespace RusZip.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IArchiveEngine _engine;

    private static readonly HashSet<string> SupportedArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zrus", ".zip", ".tar", ".gz", ".tgz", ".7z", ".rar", ".tar.gz"
    };

    public static IReadOnlyCollection<string> SupportedExtensions => SupportedArchiveExtensions;

    public static bool IsSupportedArchive(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return true;
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && SupportedArchiveExtensions.Contains(ext);
    }

    [ObservableProperty] private ArchiveBrowserViewModel _browser;
    [ObservableProperty] private CompressionSettingsViewModel _settings;
    [ObservableProperty] private OperationProgressViewModel _progress;
    [ObservableProperty] private bool _hasOpenArchive;
    [ObservableProperty] private bool _isCompressDialogVisible;
    [ObservableProperty] private string _statusText = "Ready";

    public Func<Task<string?>>? RequestExtractDestinationFolder { get; set; }

    public MainWindowViewModel(IArchiveEngine engine)
    {
        _engine = engine;
        _browser = new ArchiveBrowserViewModel();
        _settings = new CompressionSettingsViewModel();
        _progress = new OperationProgressViewModel();

        _browser.ExtractRequested += OnBrowserExtractRequestedAsync;
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

    [RelayCommand]
    public void CloseArchive()
    {
        HasOpenArchive = false;
        Browser = new ArchiveBrowserViewModel();
        Browser.ExtractRequested += OnBrowserExtractRequestedAsync;
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
            var req = new ArchiveExtractionRequest(Browser.LoadedArchivePath, destinationDirectory, Overwrite: true);
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

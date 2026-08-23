using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using RusZip.Core.Utils;
using RusZip.Desktop.Models;

namespace RusZip.Desktop.ViewModels;

public partial class QuickExtractViewModel : ObservableObject, IFileConflictResolver
{
    private readonly IArchiveEngine _engine;
    private readonly ThroughputTracker _throughputTracker = new();
    private readonly Stopwatch _stopwatch = new();
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _autoCloseCts;

    [ObservableProperty] private QuickExtractMode _mode = QuickExtractMode.ExtractHere;
    [ObservableProperty] private string _archivePath = string.Empty;
    [ObservableProperty] private string _archiveFileName = string.Empty;
    [ObservableProperty] private string _destinationDirectory = string.Empty;

    [ObservableProperty] private bool _hasStarted;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCloseButtonOnly))]
    private bool _isSuccess;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCloseButtonOnly))]
    private bool _isCancelled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCloseButtonOnly))]
    private bool _isError;

    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _currentFileName = string.Empty;
    [ObservableProperty] private double _progressPercentage;
    [ObservableProperty] private bool _isIndeterminate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedSpeed))]
    private string _speedFormatted = "-";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedEta))]
    private string _etaFormatted = "-";

    [ObservableProperty] private string _bytesProgressFormatted = "0 B / 0 B";
    [ObservableProperty] private long _processedBytes;
    [ObservableProperty] private long _totalBytes;
    [ObservableProperty] private int _processedFiles;

    [ObservableProperty] private int _filesExtracted;
    [ObservableProperty] private long _bytesExtracted;
    [ObservableProperty] private string _formattedTotalSize = "0 B";
    [ObservableProperty] private string _formattedElapsedTime = "00:00";

    [ObservableProperty] private int _autoCloseRemainingSeconds = 3;
    [ObservableProperty] private bool _isAutoCloseActive;
    [ObservableProperty] private string _autoCloseButtonText = "Close (3s)";

    public string FormattedSpeed => SpeedFormatted;
    public string FormattedEta => EtaFormatted;
    public bool ShowCloseButtonOnly => IsCancelled || IsError;

    public Func<FileConflictContext, Task<FileConflictResolution>>? RequestConflictResolution { get; set; }
    public Func<Task<string?>>? RequestFolderPicker { get; set; }
    public Func<string, Task>? OpenFolderHandler { get; set; }
    public Action? RequestClose { get; set; }
    public Func<TimeSpan, CancellationToken, Task>? DelayAsync { get; set; }

    public QuickExtractViewModel(IArchiveEngine engine, QuickExtractOptions? options = null)
    {
        _engine = engine;
        if (options != null)
        {
            Initialize(options);
        }
    }

    public void Initialize(QuickExtractOptions options)
    {
        Mode = options.Mode;
        ArchivePath = options.ArchivePath;
        ArchiveFileName = Path.GetFileName(options.ArchivePath);
        DestinationDirectory = options.DestinationDirectory ?? string.Empty;
    }

    [RelayCommand]
    public void Cancel()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            StatusMessage = "Cancelling extraction...";
            _cts.Cancel();
        }
    }

    [RelayCommand]
    public void Close()
    {
        CancelAutoCloseCountdown();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    public void CancelAutoClose()
    {
        CancelAutoCloseCountdown();
    }

    [RelayCommand]
    public async Task OpenFolderAsync()
    {
        CancelAutoCloseCountdown();
        if (string.IsNullOrEmpty(DestinationDirectory))
        {
            return;
        }

        if (OpenFolderHandler != null)
        {
            await OpenFolderHandler.Invoke(DestinationDirectory);
        }
        else
        {
            OpenFolderInFileManager(DestinationDirectory);
        }
    }

    public async Task StartExtractionAsync()
    {
        if (HasStarted)
        {
            return;
        }

        HasStarted = true;

        if (string.IsNullOrWhiteSpace(ArchivePath) || !File.Exists(ArchivePath))
        {
            IsError = true;
            ErrorMessage = $"Archive file not found: '{ArchivePath}'";
            StatusMessage = ErrorMessage;
            return;
        }

        switch (Mode)
        {
            case QuickExtractMode.ExtractHere:
                DestinationDirectory = Path.GetDirectoryName(Path.GetFullPath(ArchivePath)) ?? Directory.GetCurrentDirectory();
                break;

            case QuickExtractMode.ExtractTo:
                if (string.IsNullOrWhiteSpace(DestinationDirectory))
                {
                    if (RequestFolderPicker != null)
                    {
                        var picked = await RequestFolderPicker.Invoke();
                        if (string.IsNullOrWhiteSpace(picked))
                        {
                            IsCancelled = true;
                            StatusMessage = "Extraction cancelled.";
                            return;
                        }
                        DestinationDirectory = picked;
                    }
                    else
                    {
                        DestinationDirectory = Path.GetDirectoryName(Path.GetFullPath(ArchivePath)) ?? Directory.GetCurrentDirectory();
                    }
                }
                break;

            case QuickExtractMode.ExtractToDir:
                var parentDir = Path.GetDirectoryName(Path.GetFullPath(ArchivePath)) ?? Directory.GetCurrentDirectory();
                var baseName = ExtractionPathResolver.GetArchiveBaseName(ArchivePath);
                DestinationDirectory = ExtractionPathResolver.ResolveUniqueDestinationDirectory(parentDir, baseName);
                break;
        }

        try
        {
            Directory.CreateDirectory(DestinationDirectory);
        }
        catch (Exception ex)
        {
            IsError = true;
            ErrorMessage = $"Failed to create destination directory: {ex.Message}";
            StatusMessage = ErrorMessage;
            return;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _throughputTracker.Start();
        _stopwatch.Restart();

        IsRunning = true;
        IsSuccess = false;
        IsCancelled = false;
        IsError = false;
        ProgressPercentage = 0;
        StatusMessage = "Extracting...";

        IFileConflictResolver? conflictResolver = Mode == QuickExtractMode.ExtractToDir ? null : this;
        var progressHandler = new Progress<ProgressReport>(ReportProgress);

        try
        {
            var request = new ArchiveExtractionRequest(
                ArchivePath: ArchivePath,
                DestinationDirectory: DestinationDirectory,
                Overwrite: true,
                ConflictResolver: conflictResolver
            );

            var result = await _engine.ExtractAsync(request, progressHandler, _cts.Token);
            _stopwatch.Stop();

            IsRunning = false;
            IsSuccess = true;
            FilesExtracted = result.FilesExtracted;
            BytesExtracted = result.BytesExtracted;
            FormattedTotalSize = DataMetricsFormatter.FormatBytes(result.BytesExtracted);
            FormattedElapsedTime = $"{_stopwatch.Elapsed.Minutes:D2}:{_stopwatch.Elapsed.Seconds:D2}";
            StatusMessage = "Extraction completed successfully.";

            StartAutoCloseCountdown(3);
        }
        catch (OperationCanceledException)
        {
            _stopwatch.Stop();
            IsRunning = false;
            IsCancelled = true;
            StatusMessage = "Extraction cancelled.";
        }
        catch (Exception ex)
        {
            _stopwatch.Stop();
            IsRunning = false;
            IsError = true;
            ErrorMessage = ex.Message;
            StatusMessage = $"Extraction failed: {ex.Message}";
        }
    }

    public void ReportProgress(ProgressReport report)
    {
        CurrentFileName = EntryNameSanitizer.Sanitize(report.CurrentFileName ?? string.Empty);
        ProgressPercentage = report.Percentage;
        IsIndeterminate = report.IsIndeterminate;
        ProcessedBytes = report.ProcessedBytes;
        TotalBytes = report.TotalBytes;
        ProcessedFiles = report.ProcessedFiles;

        _throughputTracker.Update(report.ProcessedBytes, report.TotalBytes);
        BytesProgressFormatted = _throughputTracker.FormatProgress(report.TotalBytes);

        if (_throughputTracker.SmoothedSpeedBytesPerSec > 0)
        {
            SpeedFormatted = _throughputTracker.FormatSpeed();
            EtaFormatted = _throughputTracker.FormatEta(report.TotalBytes);
        }
        else if (report.TotalBytes > 0 && report.ProcessedBytes >= report.TotalBytes)
        {
            EtaFormatted = "00:00";
        }

        FormattedElapsedTime = $"{_stopwatch.Elapsed.Minutes:D2}:{_stopwatch.Elapsed.Seconds:D2}";
    }

    public async ValueTask<FileConflictResolution> ResolveConflictAsync(
        FileConflictContext context,
        CancellationToken cancellationToken = default)
    {
        if (RequestConflictResolution != null)
        {
            return await RequestConflictResolution.Invoke(context);
        }

        return FileConflictResolution.Overwrite;
    }

    public void StartAutoCloseCountdown(int seconds = 3)
    {
        CancelAutoCloseCountdown();
        _autoCloseCts = new CancellationTokenSource();
        var ct = _autoCloseCts.Token;
        AutoCloseRemainingSeconds = seconds;
        IsAutoCloseActive = true;
        UpdateAutoCloseButtonText();

        _ = RunAutoCloseLoopAsync(seconds, ct);
    }

    public void CancelAutoCloseCountdown()
    {
        if (IsAutoCloseActive)
        {
            _autoCloseCts?.Cancel();
            _autoCloseCts?.Dispose();
            _autoCloseCts = null;
            IsAutoCloseActive = false;
            UpdateAutoCloseButtonText();
        }
    }

    private async Task RunAutoCloseLoopAsync(int seconds, CancellationToken ct)
    {
        try
        {
            for (int i = seconds; i > 0; i--)
            {
                AutoCloseRemainingSeconds = i;
                UpdateAutoCloseButtonText();

                if (DelayAsync != null)
                {
                    await DelayAsync(TimeSpan.FromSeconds(1), ct);
                }
                else
                {
                    await Task.Delay(1000, ct);
                }
            }

            AutoCloseRemainingSeconds = 0;
            IsAutoCloseActive = false;
            UpdateAutoCloseButtonText();
            RequestClose?.Invoke();
        }
        catch (OperationCanceledException)
        {
            IsAutoCloseActive = false;
            UpdateAutoCloseButtonText();
        }
    }

    private void UpdateAutoCloseButtonText()
    {
        AutoCloseButtonText = IsAutoCloseActive && AutoCloseRemainingSeconds > 0
            ? $"Close ({AutoCloseRemainingSeconds}s)"
            : "Close";
    }

    public static void OpenFolderInFileManager(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", $"\"{folderPath}\"");
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", $"\"{folderPath}\"");
            }
        }
        catch
        {
            // Best-effort file manager launch
        }
    }
}

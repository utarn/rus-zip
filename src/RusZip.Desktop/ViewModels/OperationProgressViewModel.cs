using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Core.Models;

namespace RusZip.Desktop.ViewModels;

public partial class OperationProgressViewModel : ObservableObject
{
    private readonly ThroughputTracker _throughputTracker = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool _isOperationRunning;
    [ObservableProperty] private string _operationTitle = "Processing Archive...";
    [ObservableProperty] private string _currentFileName = string.Empty;
    [ObservableProperty] private double _progressPercentage;
    [ObservableProperty] private bool _isIndeterminate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedSpeed))]
    [NotifyPropertyChangedFor(nameof(TransferSpeed))]
    private string _speedFormatted = "-";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedEta))]
    [NotifyPropertyChangedFor(nameof(TimeRemaining))]
    private string _etaFormatted = "-";

    [ObservableProperty] private string _bytesProgressFormatted = "0 B / 0 B";
    [ObservableProperty] private string _statusMessage = "Preparing...";

    public string FormattedSpeed => SpeedFormatted;
    public string TransferSpeed => SpeedFormatted;
    public string FormattedEta => EtaFormatted;
    public string TimeRemaining => EtaFormatted;

    public CancellationTokenSource CreateCancellationTokenSource()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _throughputTracker.Start();
        IsOperationRunning = true;
        ProgressPercentage = 0;
        StatusMessage = "Starting...";
        SpeedFormatted = "-";
        EtaFormatted = "-";
        BytesProgressFormatted = "0 B / 0 B";
        CurrentFileName = string.Empty;
        return _cts;
    }

    [RelayCommand]
    public void Cancel()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            StatusMessage = "Cancelling operation...";
            _cts.Cancel();
        }
    }

    public void ReportProgress(ProgressReport report)
    {
        // Entry names come from untrusted archives; strip control bytes before they reach the UI.
        CurrentFileName = EntryNameSanitizer.Sanitize(report.CurrentFileName ?? string.Empty);
        ProgressPercentage = report.Percentage;
        IsIndeterminate = report.IsIndeterminate;

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
    }

    public async Task FinishOperationAsync(bool success, string? message = null)
    {
        _throughputTracker.Reset();
        StatusMessage = message ?? (success ? "Completed successfully." : "Operation cancelled or failed.");
        await Task.Delay(400);
        IsOperationRunning = false;
        _cts?.Dispose();
        _cts = null;
    }
}

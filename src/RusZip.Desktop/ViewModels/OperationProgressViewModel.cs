using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Core.Models;

namespace RusZip.Desktop.ViewModels;

public partial class OperationProgressViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    private Stopwatch? _stopwatch;
    private double _smoothedSpeedBytesPerSec;

    [ObservableProperty] private bool _isOperationRunning;
    [ObservableProperty] private string _operationTitle = "Processing Archive...";
    [ObservableProperty] private string _currentFileName = string.Empty;
    [ObservableProperty] private double _progressPercentage;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private string _speedFormatted = "-";
    [ObservableProperty] private string _etaFormatted = "-";
    [ObservableProperty] private string _bytesProgressFormatted = "0 B / 0 B";
    [ObservableProperty] private string _statusMessage = "Preparing...";

    public CancellationTokenSource CreateCancellationTokenSource()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _stopwatch = Stopwatch.StartNew();
        _smoothedSpeedBytesPerSec = 0;
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
        CurrentFileName = report.CurrentFileName ?? string.Empty;
        ProgressPercentage = report.Percentage;
        IsIndeterminate = report.IsIndeterminate;
        BytesProgressFormatted = $"{FormatBytes(report.ProcessedBytes)} / {(report.TotalBytes > 0 ? FormatBytes(report.TotalBytes) : "...")}";

        if (_stopwatch != null && _stopwatch.ElapsedMilliseconds > 200 && report.ProcessedBytes > 0)
        {
            double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
            double instantSpeed = report.ProcessedBytes / elapsedSeconds;
            _smoothedSpeedBytesPerSec = _smoothedSpeedBytesPerSec == 0
                ? instantSpeed
                : (_smoothedSpeedBytesPerSec * 0.7) + (instantSpeed * 0.3);

            SpeedFormatted = FormatSpeed(_smoothedSpeedBytesPerSec);

            if (_smoothedSpeedBytesPerSec > 1024 && report.TotalBytes > report.ProcessedBytes)
            {
                long remainingBytes = report.TotalBytes - report.ProcessedBytes;
                double secondsLeft = remainingBytes / _smoothedSpeedBytesPerSec;
                EtaFormatted = FormatEta(TimeSpan.FromSeconds(Math.Min(secondsLeft, 86400)));
            }
            else if (report.TotalBytes > 0 && report.ProcessedBytes >= report.TotalBytes)
            {
                EtaFormatted = "00:00";
            }
            else
            {
                EtaFormatted = "--:--";
            }
        }
    }

    public static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return "0 B/s";
        return $"{FormatBytes((long)bytesPerSec)}/s";
    }

    public static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalSeconds <= 0) return "00:00";
        return eta.Hours > 0 ? eta.ToString(@"hh\:mm\:ss") : eta.ToString(@"mm\:ss");
    }

    public async Task FinishOperationAsync(bool success, string? message = null)
    {
        _stopwatch?.Stop();
        StatusMessage = message ?? (success ? "Completed successfully." : "Operation cancelled or failed.");
        await Task.Delay(400);
        IsOperationRunning = false;
        _cts?.Dispose();
        _cts = null;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:0.##} {suffixes[counter]}";
    }
}

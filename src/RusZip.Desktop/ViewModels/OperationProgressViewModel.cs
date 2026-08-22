using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProCharts;
using RusZip.Core.Models;
using RusZip.Desktop.Models;

namespace RusZip.Desktop.ViewModels;

public partial class OperationProgressViewModel : ObservableObject
{
    private readonly ThroughputTracker _throughputTracker = new();
    private readonly ThroughputSeriesBuffer _throughputSeries;
    private readonly Stopwatch _telemetryStopwatch = new();
    private readonly TimeSpan _throughputSampleInterval;
    private TimeSpan _lastThroughputSampleElapsed = TimeSpan.MinValue;
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

    /// <summary>Gets the ProCharts model the overlay's velocity chart binds to.</summary>
    public ChartModel ThroughputChartModel { get; }

    /// <summary>Gets the live throughput samples backing the velocity chart (for tests/observers).</summary>
    public IReadOnlyList<ThroughputSample> ThroughputSamples => _throughputSeries.Samples;

    /// <summary>Gets the number of throughput samples currently buffered.</summary>
    public int ThroughputSampleCount => _throughputSeries.Count;

    /// <summary>Gets the rolling wall-clock window preserved by the throughput buffer.</summary>
    public TimeSpan ThroughputWindow => _throughputSeries.Window;

    /// <summary>
    /// Initializes the progress VM and its throughput velocity chart.
    /// </summary>
    /// <param name="throughputSampleInterval">
    /// Minimum wall-clock gap between buffered throughput samples. Defaults to 250 ms (4 Hz)
    /// to keep UI churn low. Tests pass a tiny interval to sample on every progress report.
    /// </param>
    public OperationProgressViewModel(TimeSpan? throughputSampleInterval = null)
    {
        _throughputSeries = new ThroughputSeriesBuffer(TimeSpan.FromSeconds(60), maxCapacity: 600);
        _throughputSampleInterval = throughputSampleInterval ?? TimeSpan.FromMilliseconds(250);

        ThroughputChartModel = new ChartModel
        {
            DataSource = new ThroughputChartDataSource(_throughputSeries)
        };
        ThroughputChartModel.Legend.IsVisible = false;
        ThroughputChartModel.CategoryAxis.Title = "Time";
        ThroughputChartModel.ValueAxis.Title = "MB/s";
        ThroughputChartModel.ValueAxis.Minimum = 0;
        ThroughputChartModel.ValueAxis.LabelFormatter = value => $"{value:0.#}";
    }

    public CancellationTokenSource CreateCancellationTokenSource()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _throughputTracker.Start();
        _telemetryStopwatch.Restart();
        _lastThroughputSampleElapsed = TimeSpan.MinValue;
        _throughputSeries.Clear();
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
        TryAddThroughputSample();

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

    /// <summary>
    /// Samples the tracker's EMA-smoothed speed into the rolling chart buffer, throttled to
    /// <see cref="_throughputSampleInterval"/>. No point is recorded until the tracker has a
    /// real speed estimate, so the curve never starts from a spurious zero.
    /// </summary>
    private void TryAddThroughputSample()
    {
        if (!_telemetryStopwatch.IsRunning)
            return;

        TimeSpan elapsed = _telemetryStopwatch.Elapsed;
        if (_lastThroughputSampleElapsed != TimeSpan.MinValue &&
            elapsed - _lastThroughputSampleElapsed < _throughputSampleInterval)
        {
            return;
        }

        double smoothedBytesPerSec = _throughputTracker.SmoothedSpeedBytesPerSec;
        if (smoothedBytesPerSec <= 0)
            return;

        _throughputSeries.Add(elapsed, smoothedBytesPerSec / (1024.0 * 1024.0));
        _lastThroughputSampleElapsed = elapsed;
    }

    public async Task FinishOperationAsync(bool success, string? message = null)
    {
        _throughputTracker.Reset();
        _telemetryStopwatch.Reset();
        _lastThroughputSampleElapsed = TimeSpan.MinValue;
        _throughputSeries.Clear();
        StatusMessage = message ?? (success ? "Completed successfully." : "Operation cancelled or failed.");
        await Task.Delay(400);
        IsOperationRunning = false;
        _cts?.Dispose();
        _cts = null;
    }
}

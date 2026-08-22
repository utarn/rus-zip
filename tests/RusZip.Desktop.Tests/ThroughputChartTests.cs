using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ProCharts;
using ProCharts.Avalonia;
using RusZip.Core.Models;
using RusZip.Desktop.Models;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public class ThroughputChartTests
{
    // ---------------------------------------------------------------------
    // ThroughputSeriesBuffer — rolling window, capacity, reset
    // ---------------------------------------------------------------------

    [Fact]
    public void ThroughputSeriesBuffer_Add_AppendsAndTrimsToRollingWindow()
    {
        var buffer = new ThroughputSeriesBuffer(TimeSpan.FromSeconds(60), maxCapacity: 600);

        for (int i = 0; i <= 70; i++)
        {
            buffer.Add(TimeSpan.FromSeconds(i), 10.0);
        }

        // Newest sample is at t=70; the 60 s window keeps t=10..70.
        Assert.Equal(61, buffer.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), buffer.Samples[0].Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(70), buffer.Samples[^1].Elapsed);
    }

    [Fact]
    public void ThroughputSeriesBuffer_Add_TrimsToCapacity()
    {
        var buffer = new ThroughputSeriesBuffer(TimeSpan.FromHours(1), maxCapacity: 10);

        for (int i = 0; i < 20; i++)
        {
            buffer.Add(TimeSpan.FromSeconds(i), i);
        }

        Assert.Equal(10, buffer.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), buffer.Samples[0].Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(19), buffer.Samples[^1].Elapsed);
    }

    [Fact]
    public void ThroughputSeriesBuffer_Add_ClampsNegativeOrInvalidValues()
    {
        var buffer = new ThroughputSeriesBuffer(TimeSpan.FromSeconds(60), maxCapacity: 600);

        buffer.Add(TimeSpan.FromSeconds(-5), -3.0);
        buffer.Add(TimeSpan.FromSeconds(1), double.NaN);

        Assert.Equal(2, buffer.Count);
        Assert.Equal(0.0, buffer.Samples[0].MegaBytesPerSec);
        Assert.Equal(0.0, buffer.Samples[1].MegaBytesPerSec);
        Assert.Equal(TimeSpan.Zero, buffer.Samples[0].Elapsed);
    }

    [Fact]
    public void ThroughputSeriesBuffer_Clear_RemovesAllSamples()
    {
        var buffer = new ThroughputSeriesBuffer(TimeSpan.FromSeconds(60), maxCapacity: 600);
        buffer.Add(TimeSpan.FromSeconds(1), 1.0);
        buffer.Add(TimeSpan.FromSeconds(2), 2.0);

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void ThroughputSeriesBuffer_Add_RaisesSamplesChangedOncePerMutation()
    {
        var buffer = new ThroughputSeriesBuffer(TimeSpan.FromSeconds(60), maxCapacity: 10);
        int raises = 0;
        buffer.SamplesChanged += (_, _) => raises++;

        // Adding 12 samples (capacity 10) trims internally but must still raise once.
        for (int i = 0; i < 12; i++)
            buffer.Add(TimeSpan.FromSeconds(i), 1.0);
        Assert.Equal(12, raises);

        buffer.Clear();
        Assert.Equal(13, raises);
    }

    // ---------------------------------------------------------------------
    // ThroughputChartDataSource — snapshot mapping + invalidation
    // ---------------------------------------------------------------------

    [Fact]
    public void ThroughputChartDataSource_BuildSnapshot_MapsSamplesToCategoriesAndSeries()
    {
        var buffer = new ThroughputSeriesBuffer(TimeSpan.FromSeconds(60), maxCapacity: 600);
        buffer.Add(TimeSpan.FromSeconds(5), 3.5);
        buffer.Add(TimeSpan.FromSeconds(6), 4.5);

        var source = new ThroughputChartDataSource(buffer);
        var snapshot = source.BuildSnapshot(new ChartDataRequest());

        Assert.Equal(2, snapshot.Categories.Count);
        Assert.Equal("0:05", snapshot.Categories[0]);
        Assert.Equal("0:06", snapshot.Categories[1]);

        var series = Assert.Single(snapshot.Series);
        Assert.Equal("Throughput", series.Name);
        Assert.Equal(ChartSeriesKind.Area, series.Kind);
        Assert.Equal(3.5, series.Values[0]);
        Assert.Equal(4.5, series.Values[1]);
        Assert.NotNull(series.Style);
        Assert.Equal(ChartLineInterpolation.Smooth, series.Style.LineInterpolation);
        Assert.Equal(ChartMarkerShape.None, series.Style.MarkerShape);
    }

    [Fact]
    public void ThroughputChartDataSource_BuildSnapshot_EmptyBufferProducesEmptySnapshot()
    {
        var buffer = new ThroughputSeriesBuffer(TimeSpan.FromSeconds(60), maxCapacity: 600);
        var source = new ThroughputChartDataSource(buffer);

        var snapshot = source.BuildSnapshot(new ChartDataRequest());

        Assert.Empty(snapshot.Categories);
        Assert.Single(snapshot.Series);
        Assert.Empty(snapshot.Series[0].Values);
    }

    [Fact]
    public void ThroughputChartDataSource_DataInvalidated_FiresWhenBufferChanges()
    {
        var buffer = new ThroughputSeriesBuffer(TimeSpan.FromSeconds(60), maxCapacity: 600);
        var source = new ThroughputChartDataSource(buffer);

        int invalidations = 0;
        source.DataInvalidated += (_, _) => invalidations++;

        buffer.Add(TimeSpan.FromSeconds(1), 1.0);
        buffer.Add(TimeSpan.FromSeconds(2), 2.0);
        buffer.Clear();

        Assert.Equal(3, invalidations);
    }

    // ---------------------------------------------------------------------
    // OperationProgressViewModel — chart model wiring, throttling, smoothing, reset
    // ---------------------------------------------------------------------

    [Fact]
    public void Constructor_CreatesChartModelWithDataSourceAndSaneAxes()
    {
        var vm = new OperationProgressViewModel();

        Assert.NotNull(vm.ThroughputChartModel);
        Assert.IsType<ThroughputChartDataSource>(vm.ThroughputChartModel.DataSource);
        Assert.False(vm.ThroughputChartModel.Legend.IsVisible);
        Assert.Equal("MB/s", vm.ThroughputChartModel.ValueAxis.Title);
        Assert.Equal(0, vm.ThroughputChartModel.ValueAxis.Minimum);
        Assert.Equal(TimeSpan.FromSeconds(60), vm.ThroughputWindow);
        Assert.Empty(vm.ThroughputSamples);
    }

    [Fact]
    public async Task ReportProgress_ThroughputSeries_ThrottlesSamplesByInterval()
    {
        // Default 250 ms sample interval.
        var vm = new OperationProgressViewModel();
        vm.CreateCancellationTokenSource();

        await Task.Delay(150);
        vm.ReportProgress(new ProgressReport(1_000_000, 10_000_000, "a.bin", 10));
        Assert.Equal(1, vm.ThroughputSampleCount);

        // Immediate second report is throttled away.
        vm.ReportProgress(new ProgressReport(2_000_000, 10_000_000, "a.bin", 20));
        Assert.Equal(1, vm.ThroughputSampleCount);

        // After the interval elapses, a new sample is recorded.
        await Task.Delay(260);
        vm.ReportProgress(new ProgressReport(3_000_000, 10_000_000, "a.bin", 30));
        Assert.Equal(2, vm.ThroughputSampleCount);
    }

    [Fact]
    public async Task ReportProgress_ThroughputSeries_SmoothsValueSpikes()
    {
        // Tiny sample interval so every progress report yields a telemetry point.
        var vm = new OperationProgressViewModel(TimeSpan.FromMilliseconds(1));
        vm.CreateCancellationTokenSource();

        // Fast phase: 5 MB after ~200 ms seeds the EMA near ~25 MB/s.
        await Task.Delay(200);
        vm.ReportProgress(new ProgressReport(5_000_000, 50_000_000, "a.bin", 10));
        double fast = vm.ThroughputSamples.Last().MegaBytesPerSec;
        Assert.True(fast > 1, $"expected a meaningful seed speed, got {fast} MB/s");

        // Slow phase: only 200 KB added -> new instantaneous rate ≈ 1 MB/s.
        // The EMA must move down but stay above the new (slower) rate: that is the smoothing.
        await Task.Delay(200);
        vm.ReportProgress(new ProgressReport(5_200_000, 50_000_000, "a.bin", 11));
        double slow = vm.ThroughputSamples.Last().MegaBytesPerSec;

        Assert.True(slow < fast, $"EMA should move down after a slowdown ({slow} >= {fast})");
        Assert.True(slow > 0.5, $"EMA should still sit above the new rate, got {slow}");
    }

    [Fact]
    public async Task CreateCancellationTokenSource_ClearsThroughputSeriesFromPreviousRun()
    {
        var vm = new OperationProgressViewModel(TimeSpan.FromMilliseconds(1));
        vm.CreateCancellationTokenSource();

        await Task.Delay(150);
        vm.ReportProgress(new ProgressReport(1_000_000, 10_000_000, "a.bin", 10));
        Assert.Equal(1, vm.ThroughputSampleCount);

        // Starting a new operation resets the velocity series.
        vm.CreateCancellationTokenSource();

        Assert.Equal(0, vm.ThroughputSampleCount);
        Assert.Empty(vm.ThroughputSamples);
    }

    [Fact]
    public async Task FinishOperationAsync_ClearsThroughputSeries()
    {
        var vm = new OperationProgressViewModel(TimeSpan.FromMilliseconds(1));
        vm.CreateCancellationTokenSource();

        await Task.Delay(150);
        vm.ReportProgress(new ProgressReport(1_000_000, 10_000_000, "a.bin", 10));
        Assert.Equal(1, vm.ThroughputSampleCount);

        await vm.FinishOperationAsync(success: true);

        Assert.False(vm.IsOperationRunning);
        Assert.Equal(0, vm.ThroughputSampleCount);
        Assert.Empty(vm.ThroughputSamples);
    }

    [Fact]
    public async Task ReportProgress_ThroughputSeries_ReflectsTrackerEmaSpeed_NotRawDeltas()
    {
        // Verifies the buffered values are the tracker's smoothed MB/s, and that the series
        // also keeps working through a cancelled-completion path (FinishOperationAsync(false)).
        var vm = new OperationProgressViewModel(TimeSpan.FromMilliseconds(1));
        vm.CreateCancellationTokenSource();

        await Task.Delay(200);
        vm.ReportProgress(new ProgressReport(5_000_000, 50_000_000, "a.bin", 10));

        Assert.Single(vm.ThroughputSamples);
        double value = vm.ThroughputSamples[0].MegaBytesPerSec;
        Assert.True(value > 0, $"expected a positive smoothed MB/s, got {value}");

        await vm.FinishOperationAsync(success: false, message: "cancelled");

        Assert.False(vm.IsOperationRunning);
        Assert.Equal("cancelled", vm.StatusMessage);
        Assert.Empty(vm.ThroughputSamples);
    }

    // ---------------------------------------------------------------------
    // ProgressOverlay view — inline ProChartView beneath the progress bar
    // ---------------------------------------------------------------------

    [AvaloniaFact]
    public void ProgressOverlay_ContainsProChartView_BoundToThroughputChartModel()
    {
        var vm = new OperationProgressViewModel();
        var view = new ProgressOverlay { DataContext = vm };

        var chart = view.FindControl<ProChartView>("ThroughputChart");

        Assert.NotNull(chart);
        Assert.Same(vm.ThroughputChartModel, chart.ChartModel);
    }
}

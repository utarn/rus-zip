using RusZip.Cli.Infrastructure;
using RusZip.Core.Models;
using Spectre.Console;

namespace RusZip.Cli.Tests;

public sealed class CliProgressColumnTests
{
    [Fact]
    public void CreateProgressColumns_WiresSharedTrackerIntoSpeedAndEtaColumns()
    {
        var tracker = new ThroughputTracker();
        var columns = CliProgressBridge.CreateProgressColumns(tracker);

        // Visual layout is unchanged: description, bar, percentage, downloaded, speed, ETA, spinner.
        Assert.Equal(7, columns.Length);
        Assert.IsType<TaskDescriptionColumn>(columns[0]);
        Assert.IsType<ProgressBarColumn>(columns[1]);
        Assert.IsType<PercentageColumn>(columns[2]);
        Assert.IsType<DownloadedColumn>(columns[3]);
        var speedColumn = Assert.IsType<TrackerSpeedColumn>(columns[4]);
        var etaColumn = Assert.IsType<TrackerEtaColumn>(columns[5]);
        Assert.IsType<SpinnerColumn>(columns[6]);

        // A fresh tracker (no sample) renders placeholders.
        Assert.Equal("?/s", speedColumn.GetDisplayText());
        Assert.Equal("--:--:--", etaColumn.GetDisplayText(10_485_760L));
    }

    [Fact]
    public async Task ApplyReport_FeedsTracker_AndColumnsRenderTrackerValues()
    {
        var tracker = new ThroughputTracker();
        tracker.Start();
        var speedColumn = new TrackerSpeedColumn(tracker);
        var etaColumn = new TrackerEtaColumn(tracker);
        var task = new ProgressTask(0, "TestOp", 100);

        // First sample needs >= 100ms elapsed to seed the EMA (see ThroughputTracker).
        await Task.Delay(150);
        CliProgressBridge.ApplyReport(tracker, task, "TestOp", new ProgressReport(
            ProcessedBytes: 1_048_576,
            TotalBytes: 10_485_760,
            CurrentFileName: "large.bin",
            Percentage: 10.0,
            IsIndeterminate: false));

        Assert.True(tracker.SmoothedSpeedBytesPerSec > 0);
        Assert.Equal(10_485_760, task.MaxValue);
        Assert.Equal(1_048_576, task.Value);

        // The columns render the tracker's math, not Spectre's internal rate.
        Assert.Equal(tracker.FormatSpeed(), speedColumn.GetDisplayText());
        Assert.Equal(tracker.FormatEta(10_485_760L), etaColumn.GetDisplayText(10_485_760L));
        Assert.NotEqual("?/s", speedColumn.GetDisplayText());
        Assert.NotEqual("--:--:--", etaColumn.GetDisplayText(10_485_760L));
    }

    [Fact]
    public async Task ApplyReport_WhenCompleted_ShowsZeroEta()
    {
        var tracker = new ThroughputTracker();
        tracker.Start();
        var etaColumn = new TrackerEtaColumn(tracker);
        var task = new ProgressTask(0, "TestOp", 100);

        await Task.Delay(150);
        CliProgressBridge.ApplyReport(tracker, task, "TestOp", new ProgressReport(
            ProcessedBytes: 10_485_760,
            TotalBytes: 10_485_760,
            CurrentFileName: "done.bin",
            Percentage: 100.0,
            IsIndeterminate: false));

        Assert.Equal("00:00", etaColumn.GetDisplayText(10_485_760L));
        Assert.Equal(10_485_760, task.MaxValue);
        Assert.False(task.IsIndeterminate);
    }

    [Fact]
    public void ApplyReport_IndeterminateReport_KeepsTaskIndeterminate()
    {
        var tracker = new ThroughputTracker();
        var task = new ProgressTask(0, "TestOp", 100);

        CliProgressBridge.ApplyReport(tracker, task, "TestOp", new ProgressReport(
            ProcessedBytes: 0,
            TotalBytes: -1,
            CurrentFileName: "unknown.tar",
            Percentage: 0,
            IsIndeterminate: true));

        Assert.True(task.IsIndeterminate);
        Assert.Contains("unknown.tar", task.Description);
    }
}

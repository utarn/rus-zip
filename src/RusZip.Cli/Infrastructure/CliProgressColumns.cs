using RusZip.Core.Models;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RusZip.Cli.Infrastructure;

/// <summary>
/// Progress column that renders throughput (bytes/sec) from a shared <see cref="ThroughputTracker"/>
/// so CLI speed math matches Desktop exactly (EMA/ETA from COR-7 / issue #50). This replaces
/// Spectre's built-in <c>TransferSpeedColumn</c>, which computes its own internal rate.
/// </summary>
internal sealed class TrackerSpeedColumn : ProgressColumn
{
    private readonly ThroughputTracker _tracker;

    public TrackerSpeedColumn(ThroughputTracker tracker)
    {
        _tracker = tracker;
    }

    /// <summary>
    /// Renders the current text without a <see cref="RenderOptions"/>/<see cref="ProgressTask"/>,
    /// exposed for unit tests.
    /// </summary>
    public string GetDisplayText()
    {
        // No real sample yet: keep the previous CLI placeholder rather than "0 B/s".
        return _tracker.SmoothedSpeedBytesPerSec <= 0
            ? "?/s"
            : _tracker.FormatSpeed();
    }

    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        return new Text(GetDisplayText());
    }
}

/// <summary>
/// Progress column that renders the remaining time from a shared <see cref="ThroughputTracker"/>
/// so CLI ETA math matches Desktop exactly (EMA/ETA from COR-7 / issue #50). This replaces
/// Spectre's built-in <c>RemainingTimeColumn</c>, which computes its own internal rate.
/// </summary>
internal sealed class TrackerEtaColumn : ProgressColumn
{
    private readonly ThroughputTracker _tracker;

    public TrackerEtaColumn(ThroughputTracker tracker)
    {
        _tracker = tracker;
    }

    protected override bool NoWrap => true;

    /// <summary>
    /// Renders the current text without a <see cref="RenderOptions"/>/<see cref="ProgressTask"/>,
    /// exposed for unit tests.
    /// </summary>
    public string GetDisplayText(long totalBytes)
    {
        var eta = _tracker.EstimatedTimeRemaining(totalBytes);
        if (!eta.HasValue)
        {
            return "--:--:--";
        }

        if (eta.Value.TotalHours > 99)
        {
            return "**:**:**";
        }

        return DataMetricsFormatter.FormatEta(eta.Value);
    }

    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        return new Text(GetDisplayText((long)task.MaxValue), Color.Blue);
    }

    public override int? GetColumnWidth(RenderOptions options)
    {
        return 8;
    }
}

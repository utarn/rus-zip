using RusZip.Core.Models;
using Spectre.Console;

namespace RusZip.Cli.Infrastructure;

public static class CliProgressBridge
{
    public static async Task ExecuteWithProgressAsync(
        string title,
        bool isJson,
        Func<IProgress<ProgressReport>?, CancellationToken, Task> operation,
        CancellationToken ct = default)
    {
        if (isJson)
        {
            await operation(null, ct);
            return;
        }

        // One tracker shared by the reporter and the speed/ETA columns so CLI math matches
        // Desktop exactly (EMA/ETA from COR-7 / issue #50). Start it up front, mirroring
        // OperationProgressViewModel, so the first report after a real delay seeds a sample.
        var tracker = new ThroughputTracker();
        tracker.Start();

        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(CreateProgressColumns(tracker))
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[bold cyan]{Markup.Escape(title)}[/]", maxValue: 100);

                var progress = new Progress<ProgressReport>(report => ApplyReport(tracker, task, title, report));

                await operation(progress, ct);

                task.Value = task.MaxValue;
                task.Description = $"[green]✔ {Markup.Escape(title)} complete[/]";
            });
    }

    /// <summary>
    /// Builds the progress column set. The speed and ETA columns read the shared
    /// <see cref="ThroughputTracker"/> instead of Spectre's internal rate math.
    /// </summary>
    internal static ProgressColumn[] CreateProgressColumns(ThroughputTracker tracker)
    {
        return
        [
            new TaskDescriptionColumn(),
            new ProgressBarColumn(),
            new PercentageColumn(),
            new DownloadedColumn(),
            new TrackerSpeedColumn(tracker),
            new TrackerEtaColumn(tracker),
            new SpinnerColumn(),
        ];
    }

    /// <summary>
    /// Feeds a progress report into the tracker and updates the Spectre task. The tracker
    /// update is unconditional (mirroring <c>OperationProgressViewModel</c>) so indeterminate
    /// reports still contribute speed samples when byte counters are available.
    /// </summary>
    internal static void ApplyReport(ThroughputTracker tracker, ProgressTask task, string title, ProgressReport report)
    {
        tracker.Update(report.ProcessedBytes, report.TotalBytes);

        if (report.IsIndeterminate || report.TotalBytes <= 0)
        {
            task.IsIndeterminate = true;
            if (!string.IsNullOrEmpty(report.CurrentFileName))
            {
                task.Description = $"[cyan]{Markup.Escape(title)}:[/] [dim]{Markup.Escape(EntryNameSanitizer.Sanitize(Path.GetFileName(report.CurrentFileName)))}[/]";
            }
            return;
        }

        task.IsIndeterminate = false;
        task.MaxValue = report.TotalBytes;
        task.Value = Math.Clamp(report.ProcessedBytes, 0, report.TotalBytes);

        var filePart = !string.IsNullOrEmpty(report.CurrentFileName)
            ? $" [dim]({Markup.Escape(EntryNameSanitizer.Sanitize(Path.GetFileName(report.CurrentFileName)))})[/]"
            : string.Empty;

        // Metadata-derived pre-scan totals are spoofable, so they are surfaced as
        // estimates (ADR-0007); enforcement never reads them.
        var estimatePart = report.IsTotalEstimate
            ? " [dim](estimate)[/]"
            : string.Empty;

        task.Description = $"[cyan]{Markup.Escape(title)}:[/]{filePart}{estimatePart}";
    }
}

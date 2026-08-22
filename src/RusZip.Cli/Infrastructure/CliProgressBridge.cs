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

        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new DownloadedColumn(),
                new TransferSpeedColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[bold cyan]{Markup.Escape(title)}[/]", maxValue: 100);

                var progress = new Progress<ProgressReport>(report =>
                {
                    if (report.IsIndeterminate || report.TotalBytes <= 0)
                    {
                        task.IsIndeterminate = true;
                        if (!string.IsNullOrEmpty(report.CurrentFileName))
                        {
                            task.Description = $"[cyan]{Markup.Escape(title)}:[/] [dim]{Markup.Escape(Path.GetFileName(report.CurrentFileName))}[/]";
                        }
                    }
                    else
                    {
                        task.IsIndeterminate = false;
                        task.MaxValue = report.TotalBytes;
                        task.Value = Math.Clamp(report.ProcessedBytes, 0, report.TotalBytes);

                        var filePart = !string.IsNullOrEmpty(report.CurrentFileName)
                            ? $" [dim]({Markup.Escape(Path.GetFileName(report.CurrentFileName))})[/]"
                            : string.Empty;

                        task.Description = $"[cyan]{Markup.Escape(title)}:[/]{filePart}";
                    }
                });

                await operation(progress, ct);

                task.Value = task.MaxValue;
                task.Description = $"[green]✔ {Markup.Escape(title)} complete[/]";
            });
    }
}

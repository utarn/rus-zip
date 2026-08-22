using System.ComponentModel;
using RusZip.Cli.Commands.Settings;
using RusZip.Cli.Infrastructure;
using RusZip.Cli.Models;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace RusZip.Cli.Commands;

public sealed class ListSettings : JsonCommandSettings
{
    [CommandArgument(0, "<ARCHIVE>")]
    [Description("Path to the archive file (.zrus, .zip, .rar, .7z, .gz, .tar.gz).")]
    public string ArchivePath { get; init; } = string.Empty;
}

public sealed class ListCommand(IArchiveEngine engine) : AsyncCommand<ListSettings>
{
    private readonly IArchiveEngine _engine = engine;

    public override async Task<int> ExecuteAsync(CommandContext context, ListSettings settings)
    {
        return await CliCommandRunner.RunAsync(
            "Listing",
            settings.Json,
            async (progress, ct) =>
            {
                if (string.IsNullOrWhiteSpace(settings.ArchivePath))
                {
                    throw new ArgumentException("Archive path cannot be empty.");
                }

                var archivePath = Path.GetFullPath(settings.ArchivePath);
                if (!File.Exists(archivePath))
                {
                    throw new FileNotFoundException($"Archive file '{settings.ArchivePath}' was not found.", archivePath);
                }

                var formatDescriptor = ArchiveFormatRegistry.Detect(archivePath);
                var entries = await _engine.ListEntriesAsync(archivePath);

                string formatStr = formatDescriptor.PrimaryExtension.TrimStart('.');
                var entryItems = entries.Select(e => new ListEntryItem(
                    Path: e.RelativePath,
                    IsDirectory: e.IsDirectory,
                    UncompressedSize: e.UncompressedSize,
                    CompressedSize: e.CompressedSize,
                    LastModified: e.LastModified
                )).ToList();

                return new ListResult(
                    Success: true,
                    ArchivePath: archivePath,
                    Format: formatStr,
                    TotalEntries: entryItems.Count,
                    TotalUncompressedBytes: entryItems.Sum(e => e.UncompressedSize),
                    Entries: entryItems
                );
            },
            renderConsoleSummary: (result, elapsedMs) =>
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .Title($"[bold cyan]{Markup.Escape(Path.GetFileName(result.ArchivePath))}[/]")
                    .AddColumn(new TableColumn("Path").LeftAligned())
                    .AddColumn(new TableColumn("Size").RightAligned())
                    .AddColumn(new TableColumn("Modified").RightAligned());

                foreach (var entry in result.Entries)
                {
                    string icon = entry.IsDirectory ? "[yellow]📁[/]" : "[blue]📄[/]";
                    string size = entry.IsDirectory ? "-" : DataMetricsFormatter.FormatBytes(entry.UncompressedSize);
                    string modified = entry.LastModified?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

                    table.AddRow($"{icon} {Markup.Escape(entry.Path)}", size, modified);
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"\n[dim]Total: {result.TotalEntries:N0} entries ({DataMetricsFormatter.FormatBytes(result.TotalUncompressedBytes)})[/]");
            }
        );
    }
}

using System.ComponentModel;
using RusZip.Cli.Commands.Settings;
using RusZip.Cli.Infrastructure;
using RusZip.Cli.Models;
using RusZip.Core.Abstractions;
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
        var archivePath = Path.GetFullPath(settings.ArchivePath);
        if (!File.Exists(archivePath))
        {
            if (settings.Json)
                CliJsonSerializer.EmitError("ARCHIVE_NOT_FOUND", $"Archive file '{settings.ArchivePath}' was not found.");
            else
                AnsiConsole.MarkupLine($"[red]Error:[/] Archive file '{Markup.Escape(settings.ArchivePath)}' not found.");
            return 2;
        }

        try
        {
            var entries = await _engine.ListEntriesAsync(archivePath);

            if (settings.Json)
            {
                var entryItems = entries.Select(e => new ListEntryItem(
                    Path: e.RelativePath,
                    IsDirectory: e.IsDirectory,
                    UncompressedSize: e.UncompressedSize,
                    CompressedSize: e.CompressedSize,
                    LastModified: e.LastModified
                )).ToList();

                CliJsonSerializer.Emit(new ListResult(
                    Success: true,
                    ArchivePath: archivePath,
                    Format: Path.GetExtension(archivePath).TrimStart('.').ToLowerInvariant(),
                    TotalEntries: entryItems.Count,
                    TotalUncompressedBytes: entryItems.Sum(e => e.UncompressedSize),
                    Entries: entryItems
                ));
            }
            else
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .Title($"[bold cyan]{Markup.Escape(Path.GetFileName(archivePath))}[/]")
                    .AddColumn(new TableColumn("Path").LeftAligned())
                    .AddColumn(new TableColumn("Size").RightAligned())
                    .AddColumn(new TableColumn("Modified").RightAligned());

                foreach (var entry in entries)
                {
                    string icon = entry.IsDirectory ? "[yellow]📁[/]" : "[blue]📄[/]";
                    string size = entry.IsDirectory ? "-" : CliProgressBridge.FormatBytes(entry.UncompressedSize);
                    string modified = entry.LastModified?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

                    table.AddRow($"{icon} {Markup.Escape(entry.RelativePath)}", size, modified);
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"\n[dim]Total: {entries.Count:N0} entries ({CliProgressBridge.FormatBytes(entries.Sum(e => e.UncompressedSize))})[/]");
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (settings.Json)
                CliJsonSerializer.EmitError("LIST_FAILED", ex.Message, ex.StackTrace);
            else
                AnsiConsole.WriteException(ex);
            return 1;
        }
    }
}

using System.ComponentModel;
using System.Security;
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
        if (string.IsNullOrWhiteSpace(settings.ArchivePath))
        {
            if (settings.Json)
                CliJsonSerializer.EmitError("ARGUMENT_ERROR", "Archive path cannot be empty.");
            else
                AnsiConsole.MarkupLine("[red]Error:[/] Archive path cannot be empty.");
            return 2;
        }

        var archivePath = Path.GetFullPath(settings.ArchivePath);
        if (!File.Exists(archivePath))
        {
            if (settings.Json)
                CliJsonSerializer.EmitError("ARCHIVE_NOT_FOUND", $"Archive file '{settings.ArchivePath}' was not found.");
            else
                AnsiConsole.MarkupLine($"[red]Error:[/] Archive file '{Markup.Escape(settings.ArchivePath)}' not found.");
            return 2;
        }

        ArchiveFormatDescriptor formatDescriptor;
        try
        {
            formatDescriptor = ArchiveFormatRegistry.Detect(archivePath);
        }
        catch (NotSupportedException ex)
        {
            if (settings.Json)
                CliJsonSerializer.EmitError("UNSUPPORTED_FORMAT", ex.Message);
            else
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }

        try
        {
            var entries = await _engine.ListEntriesAsync(archivePath);

            string formatStr = formatDescriptor.PrimaryExtension.TrimStart('.');

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
                    Format: formatStr,
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
                    string size = entry.IsDirectory ? "-" : DataMetricsFormatter.FormatBytes(entry.UncompressedSize);
                    string modified = entry.LastModified?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

                    table.AddRow($"{icon} {Markup.Escape(entry.RelativePath)}", size, modified);
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"\n[dim]Total: {entries.Count:N0} entries ({DataMetricsFormatter.FormatBytes(entries.Sum(e => e.UncompressedSize))})[/]");
            }

            return 0;
        }
        catch (SecurityException secEx)
        {
            if (settings.Json)
                CliJsonSerializer.EmitError("SECURITY_VIOLATION", secEx.Message, secEx.StackTrace);
            else
                AnsiConsole.MarkupLine($"[red]Security Violation:[/] {Markup.Escape(secEx.Message)}");
            return 1;
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

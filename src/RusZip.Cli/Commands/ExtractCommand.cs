using System.ComponentModel;
using System.Diagnostics;
using RusZip.Cli.Commands.Settings;
using RusZip.Cli.Infrastructure;
using RusZip.Cli.Models;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace RusZip.Cli.Commands;

public sealed class ExtractSettings : JsonCommandSettings
{
    [CommandArgument(0, "<ARCHIVE>")]
    [Description("Path to the archive file (.zrus, .zip, .rar, .7z, .gz, .tar.gz).")]
    public string ArchivePath { get; init; } = string.Empty;

    [CommandOption("-o|--output <DESTINATION>")]
    [Description("Directory to extract contents into (defaults to current directory).")]
    public string? DestinationPath { get; init; }

    [CommandOption("--overwrite")]
    [Description("Overwrite existing files at destination.")]
    [DefaultValue(true)]
    public bool Overwrite { get; init; } = true;
}

public sealed class ExtractCommand(IArchiveEngine engine) : AsyncCommand<ExtractSettings>
{
    private readonly IArchiveEngine _engine = engine;

    public override async Task<int> ExecuteAsync(CommandContext context, ExtractSettings settings)
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

        var destination = settings.DestinationPath != null
            ? Path.GetFullPath(settings.DestinationPath)
            : Directory.GetCurrentDirectory();

        var request = new ArchiveExtractionRequest(archivePath, destination, settings.Overwrite);
        var sw = Stopwatch.StartNew();

        try
        {
            await CliProgressBridge.ExecuteWithProgressAsync(
                "Extracting",
                settings.Json,
                async (prog, ct) => await _engine.ExtractAsync(request, prog, ct)
            );

            sw.Stop();

            var entries = await _engine.ListEntriesAsync(archivePath);
            int fileCount = entries.Count(e => !e.IsDirectory);
            long totalBytes = entries.Where(e => !e.IsDirectory).Sum(e => e.UncompressedSize);

            if (settings.Json)
            {
                CliJsonSerializer.Emit(new ExtractResult(
                    Success: true,
                    ArchivePath: archivePath,
                    DestinationPath: destination,
                    ExtractedFiles: fileCount,
                    TotalBytes: totalBytes,
                    ElapsedMilliseconds: sw.ElapsedMilliseconds
                ));
            }
            else
            {
                var summaryTable = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Metric")
                    .AddColumn("Value")
                    .AddRow("Archive", Markup.Escape(archivePath))
                    .AddRow("Destination", Markup.Escape(destination))
                    .AddRow("Files Extracted", fileCount.ToString("N0"))
                    .AddRow("Total Extracted Size", CliProgressBridge.FormatBytes(totalBytes))
                    .AddRow("Time Elapsed", $"{sw.ElapsedMilliseconds} ms");

                AnsiConsole.Write(new Panel(summaryTable).Header("[bold green]Extraction Summary[/]"));
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (settings.Json)
                CliJsonSerializer.EmitError("EXTRACT_FAILED", ex.Message, ex.StackTrace);
            else
                AnsiConsole.WriteException(ex);
            return 1;
        }
    }
}

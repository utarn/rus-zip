using System.ComponentModel;
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

    [CommandOption("-f|--force|--overwrite")]
    [Description("Overwrite existing files at destination.")]
    [DefaultValue(true)]
    public bool Overwrite { get; init; } = true;
}

public sealed class ExtractCommand(IArchiveEngine engine) : AsyncCommand<ExtractSettings>
{
    private readonly IArchiveEngine _engine = engine;

    public override async Task<int> ExecuteAsync(CommandContext context, ExtractSettings settings)
    {
        return await CliCommandRunner.RunAsync(
            "Extracting",
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
                if (!formatDescriptor.CanDecompress)
                {
                    throw new NotSupportedException($"Extraction of archive format '{formatDescriptor.Format}' is not supported.");
                }

                var destination = settings.DestinationPath != null
                    ? Path.GetFullPath(settings.DestinationPath)
                    : Directory.GetCurrentDirectory();

                var request = new ArchiveExtractionRequest(archivePath, destination, settings.Overwrite);

                await _engine.ExtractAsync(request, progress, ct);

                var entries = await _engine.ListEntriesAsync(archivePath);
                int fileCount = entries.Count(e => !e.IsDirectory);
                long totalBytes = entries.Where(e => !e.IsDirectory).Sum(e => e.UncompressedSize);

                return new ExtractResult(
                    Success: true,
                    ArchivePath: archivePath,
                    DestinationPath: destination,
                    ExtractedFiles: fileCount,
                    TotalBytes: totalBytes,
                    ElapsedMilliseconds: 0
                );
            },
            renderConsoleSummary: (result, elapsedMs) =>
            {
                var summaryTable = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Metric")
                    .AddColumn("Value")
                    .AddRow("Archive", Markup.Escape(result.ArchivePath))
                    .AddRow("Destination", Markup.Escape(result.DestinationPath))
                    .AddRow("Files Extracted", result.ExtractedFiles.ToString("N0"))
                    .AddRow("Total Extracted Size", DataMetricsFormatter.FormatBytes(result.TotalBytes))
                    .AddRow("Time Elapsed", $"{elapsedMs} ms");

                AnsiConsole.Write(new Panel(summaryTable).Header("[bold green]Extraction Summary[/]"));
            }
        );
    }
}

using System.ComponentModel;
using RusZip.Cli.Commands.Settings;
using RusZip.Cli.Infrastructure;
using RusZip.Cli.Models;
using RusZip.Core.Abstractions;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace RusZip.Cli.Commands;

public sealed class ExtractSettings : JsonCommandSettings
{
    [CommandArgument(0, "<ARCHIVE>")]
    [Description("Path to the archive file (.zrus, .zip, .rar, .7z, .gz, .tar.gz, .zst).")]
    public string ArchivePath { get; init; } = string.Empty;

    [CommandOption("-o|--output <DESTINATION>")]
    [Description("Directory to extract contents into (defaults to current directory).")]
    public string? DestinationPath { get; init; }

    [CommandOption("-f|--force|--overwrite")]
    [Description("Overwrite existing files at destination (default).")]
    [DefaultValue(true)]
    public bool Overwrite { get; init; } = true;

    [CommandOption("--no-overwrite")]
    [Description("Do not overwrite existing files; extraction aborts (exit 1) naming the conflicting path if a destination file already exists.")]
    public bool NoOverwrite { get; init; }

    [CommandOption("-c|--conflict <POLICY>")]
    [Description("File conflict resolution policy: overwrite, skip, abort.")]
    public string? ConflictPolicy { get; init; }

    [CommandOption("--max-uncompressed-size <SIZE>")]
    [Description("Maximum cumulative uncompressed output before extraction aborts. Accepts bytes or human units (e.g. 10GB, 500MB, 1KB); 0 = unlimited. Default: 64GB.")]
    public string? MaxUncompressedSize { get; init; }

    [CommandOption("--max-entries <COUNT>")]
    [Description("Maximum number of entries to process before extraction aborts; 0 = unlimited. Default: 1,000,000.")]
    public long? MaxEntries { get; init; }

    [CommandOption("-p|--password <PASSWORD>")]
    [Description("Password for decrypting encrypted archives.")]
    public string? Password { get; init; }
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

                IFileConflictResolver? conflictResolver = null;
                if (settings.ConflictPolicy is not null)
                {
                    var policy = settings.ConflictPolicy.Trim().ToLowerInvariant();
                    conflictResolver = policy switch
                    {
                        "overwrite" => FixedPolicyConflictResolver.OverwriteAll,
                        "skip" => FixedPolicyConflictResolver.SkipAll,
                        "abort" => FixedPolicyConflictResolver.Abort,
                        _ => throw new ArgumentException(
                            $"Invalid conflict policy '{settings.ConflictPolicy}'. Valid policies: overwrite, skip, abort.")
                    };
                }

                var password = settings.Password;
                if (string.IsNullOrEmpty(password) && await _engine.IsEncryptedAsync(archivePath, ct))
                {
                    if (settings.Json || Console.IsInputRedirected || !AnsiConsole.Profile.Capabilities.Interactive)
                    {
                        throw new ArchiveIntegrityException("Password required for encrypted archive.");
                    }

                    password = AnsiConsole.Prompt(
                        new TextPrompt<string>("Enter archive password:")
                            .PromptStyle("teal")
                            .Secret());
                }

                var request = new ArchiveExtractionRequest(
                    archivePath,
                    destination,
                    settings.Overwrite && !settings.NoOverwrite,
                    BuildLimits(settings),
                    ConflictResolver: conflictResolver,
                    Password: password);

                var result = await _engine.ExtractAsync(request, progress, ct);

                return new ExtractResult(
                    Success: true,
                    ArchivePath: archivePath,
                    DestinationPath: destination,
                    ExtractedFiles: result.FilesExtracted,
                    TotalBytes: result.BytesExtracted,
                    ElapsedMilliseconds: 0
                );
            },
            renderConsoleSummary: (result, elapsedMs) =>
            {
                var summaryTable = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Metric")
                    .AddColumn("Value")
                    .AddRow("Archive", Markup.Escape(EntryNameSanitizer.Sanitize(result.ArchivePath)))
                    .AddRow("Destination", Markup.Escape(EntryNameSanitizer.Sanitize(result.DestinationPath)))
                    .AddRow("Files Extracted", result.ExtractedFiles.ToString("N0"))
                    .AddRow("Total Extracted Size", DataMetricsFormatter.FormatBytes(result.TotalBytes))
                    .AddRow("Time Elapsed", $"{elapsedMs} ms");

                AnsiConsole.Write(new Panel(summaryTable).Header("[bold green]Extraction Summary[/]"));
            },
            verboseErrors: settings.VerboseErrors
        );
    }

    private static ExtractionLimits BuildLimits(ExtractSettings settings)
    {
        long? maxBytes = SafeArchiveExtractor.DefaultMaxCumulativeUncompressedBytes;
        if (settings.MaxUncompressedSize is not null)
        {
            if (!DataSizeParser.TryParse(settings.MaxUncompressedSize, out var parsed))
            {
                throw new ArgumentException(
                    $"Invalid value for --max-uncompressed-size: '{settings.MaxUncompressedSize}'. Use bytes or human units such as 10GB, 500MB, 1KB (0 = unlimited).");
            }

            maxBytes = parsed > 0 ? parsed : null; // 0 = unlimited
        }

        int? maxEntries = SafeArchiveExtractor.DefaultMaxEntryCount;
        if (settings.MaxEntries.HasValue)
        {
            if (settings.MaxEntries.Value is < 0 or > int.MaxValue)
            {
                throw new ArgumentException($"Invalid value for --max-entries: {settings.MaxEntries.Value}. Expected a non-negative integer (0 = unlimited).");
            }

            maxEntries = settings.MaxEntries.Value > 0 ? (int)settings.MaxEntries.Value : null; // 0 = unlimited
        }

        return new ExtractionLimits(maxBytes, maxEntries);
    }
}

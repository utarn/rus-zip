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

public sealed class AppendSettings : JsonCommandSettings
{
    [CommandArgument(0, "<ARCHIVE>")]
    [Description("Path to the target archive file (.zrus, .zip).")]
    public string ArchivePath { get; init; } = string.Empty;

    [CommandArgument(1, "<SOURCES>")]
    [Description("Files or directories to append.")]
    public string[] SourcePaths { get; init; } = [];

    [CommandOption("-l|--level <LEVEL>")]
    [Description("Compression level (0-9 for .zip where 0 = Store, 1-22 for .zrus). Default: 9.")]
    public int? Level { get; init; }

    [CommandOption("-p|--profile <PROFILE>")]
    [Description("Compression profile for .zrus: fast (3), balanced (9), high (15), ultra (22).")]
    public string? Profile { get; init; }

    [CommandOption("-u|--update-only")]
    [Description("Only replace existing entries if the source file is strictly newer.")]
    public bool UpdateOnly { get; init; }

    [CommandOption("--password <PASSWORD>")]
    [Description("Password for encrypting or unlocking the archive.")]
    public string? Password { get; init; }
}

public sealed class AppendCommand(IArchiveEngine engine) : AsyncCommand<AppendSettings>
{
    private readonly IArchiveEngine _engine = engine;

    public override async Task<int> ExecuteAsync(CommandContext context, AppendSettings settings)
    {
        return await CliCommandRunner.RunAsync(
            "Appending",
            settings.Json,
            async (progress, ct) =>
            {
                if (string.IsNullOrWhiteSpace(settings.ArchivePath))
                {
                    throw new ArgumentException("Archive path cannot be empty.");
                }

                if (settings.SourcePaths is null or { Length: 0 })
                {
                    throw new ArgumentException("At least one source path must be specified.");
                }

                var archivePath = Path.GetFullPath(settings.ArchivePath);
                if (!File.Exists(archivePath))
                {
                    throw new FileNotFoundException($"Archive file '{settings.ArchivePath}' was not found.", archivePath);
                }

                var formatDescriptor = ArchiveFormatRegistry.Detect(archivePath);
                if (formatDescriptor.Format == ArchiveFormat.Zst)
                {
                    throw new NotSupportedException("Appending is not supported for single-file streams.");
                }

                if (!formatDescriptor.CanCompress)
                {
                    throw new NotSupportedException($"Appending to archive format '{formatDescriptor.Format}' is not supported.");
                }

                if (!string.IsNullOrWhiteSpace(settings.Profile))
                {
                    var normalizedProfile = settings.Profile.Trim().ToLowerInvariant();
                    if (normalizedProfile is not ("fast" or "balanced" or "high" or "ultra"))
                    {
                        throw new ArgumentException($"Invalid compression profile '{settings.Profile}'. Valid profiles: fast, balanced, high, ultra.");
                    }
                }

                var compressionLevel = CompressionProfiles.ResolveLevel(settings.Profile, settings.Level);
                if (compressionLevel < formatDescriptor.MinCompressionLevel || compressionLevel > formatDescriptor.MaxCompressionLevel)
                {
                    var formatName = formatDescriptor.PrimaryExtension.TrimStart('.');
                    string rangeNote = formatDescriptor.MinCompressionLevel == 0 ? " (0 = Store)" : "";
                    throw new ArgumentException(
                        $"Compression level {compressionLevel} is not valid for .{formatName} archives. Valid range: {formatDescriptor.MinCompressionLevel}-{formatDescriptor.MaxCompressionLevel}{rangeNote}.");
                }

                var request = new ArchiveAppendRequest(
                    archivePath,
                    settings.SourcePaths,
                    compressionLevel,
                    settings.UpdateOnly,
                    Password: settings.Password);

                return await _engine.AppendAsync(request, progress, ct);
            },
            renderConsoleSummary: (result, elapsedMs) =>
            {
                var summaryTable = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Metric")
                    .AddColumn("Value")
                    .AddRow("Archive Path", Markup.Escape(EntryNameSanitizer.Sanitize(result.ArchivePath)))
                    .AddRow("Added Files", result.AddedFiles.ToString("N0"))
                    .AddRow("Updated Files", result.UpdatedFiles.ToString("N0"))
                    .AddRow("Retained Files", result.RetainedFiles.ToString("N0"))
                    .AddRow("Skipped Files", result.SkippedFiles.ToString("N0"))
                    .AddRow("Total Files", result.TotalFiles.ToString("N0"))
                    .AddRow("Uncompressed Size", DataMetricsFormatter.FormatBytes(result.UncompressedBytes))
                    .AddRow("Compressed Size", DataMetricsFormatter.FormatBytes(result.CompressedBytes))
                    .AddRow("Ratio", $"{result.CompressionRatio * 100:N1}%")
                    .AddRow("Time Elapsed", $"{elapsedMs} ms");

                AnsiConsole.Write(new Panel(summaryTable).Header("[bold green]Append Summary[/]"));
            },
            verboseErrors: settings.VerboseErrors
        );
    }
}

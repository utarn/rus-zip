using System.ComponentModel;
using RusZip.Cli.Commands.Settings;
using RusZip.Cli.Infrastructure;
using RusZip.Cli.Models;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace RusZip.Cli.Commands;

public sealed class CompressSettings : JsonCommandSettings
{
    [CommandArgument(0, "<SOURCE>")]
    [Description("File or directory to compress.")]
    public string SourcePath { get; init; } = string.Empty;

    [CommandArgument(1, "[DESTINATION]")]
    [Description("Destination archive path (defaults to <SOURCE>.zrus).")]
    public string? DestinationPath { get; init; }

    [CommandOption("-l|--level <LEVEL>")]
    [Description("Compression level (0-9 for .zip where 0 = Store, 1-22 for .zrus). Default: 9.")]
    public int? Level { get; init; }

    [CommandOption("-p|--profile <PROFILE>")]
    [Description("Compression profile for .zrus: fast (3), balanced (9), high (15), ultra (22).")]
    public string? Profile { get; init; }
}

public sealed class CompressCommand(IArchiveEngine engine) : AsyncCommand<CompressSettings>
{
    private readonly IArchiveEngine _engine = engine;

    public override async Task<int> ExecuteAsync(CommandContext context, CompressSettings settings)
    {
        return await CliCommandRunner.RunAsync(
            "Compressing",
            settings.Json,
            async (progress, ct) =>
            {
                if (string.IsNullOrWhiteSpace(settings.SourcePath))
                {
                    throw new ArgumentException("Source path cannot be empty.");
                }

                var source = Path.GetFullPath(settings.SourcePath);
                if (!File.Exists(source) && !Directory.Exists(source))
                {
                    throw new FileNotFoundException($"Source path '{settings.SourcePath}' does not exist.", source);
                }

                if (!string.IsNullOrWhiteSpace(settings.Profile))
                {
                    var normalizedProfile = settings.Profile.Trim().ToLowerInvariant();
                    if (normalizedProfile is not ("fast" or "balanced" or "high" or "ultra"))
                    {
                        throw new ArgumentException($"Invalid compression profile '{settings.Profile}'. Valid profiles: fast, balanced, high, ultra.");
                    }
                }

                var destination = settings.DestinationPath ?? (source + ".zrus");
                destination = Path.GetFullPath(destination);

                var formatDescriptor = ArchiveFormatRegistry.Detect(destination);
                if (!formatDescriptor.CanCompress)
                {
                    var supportedCreationFormats = string.Join(", ", ArchiveFormatRegistry.CompressibleFormats.Select(f => f.PrimaryExtension));
                    throw new NotSupportedException($"Creation of archive format '{formatDescriptor.Format}' is not supported. Supported creation formats: {supportedCreationFormats}");
                }

                // F-16: level validation is per destination format. The registry models the real
                // range for each format (.zip 0-9 with 0 = Store, .zrus 1-22), so `-l 15 x.zip`
                // is rejected instead of silently capping, and `-l 0 x.zip` maps to Store.
                var compressionLevel = CompressionProfiles.ResolveLevel(settings.Profile, settings.Level);
                if (compressionLevel < formatDescriptor.MinCompressionLevel || compressionLevel > formatDescriptor.MaxCompressionLevel)
                {
                    var formatName = formatDescriptor.PrimaryExtension.TrimStart('.');
                    string rangeNote = formatDescriptor.MinCompressionLevel == 0 ? " (0 = Store)" : "";
                    throw new ArgumentException(
                        $"Compression level {compressionLevel} is not valid for .{formatName} archives. Valid range: {formatDescriptor.MinCompressionLevel}-{formatDescriptor.MaxCompressionLevel}{rangeNote}.");
                }

                var request = new ArchiveCompressionRequest(source, destination, compressionLevel);

                await _engine.CompressAsync(request, progress, ct);

                var destInfo = new FileInfo(destination);
                long uncompressedSize = Directory.Exists(source)
                    ? Directory.GetFiles(source, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
                    : new FileInfo(source).Length;
                int fileCount = Directory.Exists(source)
                    ? Directory.GetFiles(source, "*", SearchOption.AllDirectories).Length
                    : 1;

                double ratio = uncompressedSize > 0 ? (double)destInfo.Length / uncompressedSize : 1.0;
                string formatStr = formatDescriptor.PrimaryExtension.TrimStart('.');

                return new CompressResult(
                    Success: true,
                    SourcePath: source,
                    ArchivePath: destination,
                    Format: formatStr,
                    TotalFiles: fileCount,
                    UncompressedBytes: uncompressedSize,
                    CompressedBytes: destInfo.Length,
                    CompressionRatio: Math.Round(ratio, 4),
                    ElapsedMilliseconds: 0
                );
            },
            renderConsoleSummary: (result, elapsedMs) =>
            {
                var summaryTable = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Metric")
                    .AddColumn("Value")
                    .AddRow("Archive Path", Markup.Escape(EntryNameSanitizer.Sanitize(result.ArchivePath)))
                    .AddRow("Total Files", result.TotalFiles.ToString("N0"))
                    .AddRow("Uncompressed Size", DataMetricsFormatter.FormatBytes(result.UncompressedBytes))
                    .AddRow("Compressed Size", DataMetricsFormatter.FormatBytes(result.CompressedBytes))
                    .AddRow("Ratio", $"{result.CompressionRatio * 100:N1}%")
                    .AddRow("Time Elapsed", $"{elapsedMs} ms");

                AnsiConsole.Write(new Panel(summaryTable).Header("[bold green]Compression Summary[/]"));
            },
            verboseErrors: settings.VerboseErrors
        );
    }
}

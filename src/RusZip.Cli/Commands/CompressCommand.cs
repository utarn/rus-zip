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

public sealed class CompressSettings : JsonCommandSettings
{
    [CommandArgument(0, "<SOURCES>")]
    [Description("Files or directories to compress.")]
    public string[] SourcePaths { get; init; } = [];

    [CommandOption("-o|--output <PATH>")]
    [Description("Destination archive path (defaults to <SOURCE>.zrus when single source).")]
    public string? OutputPath { get; init; }

    // Backwards compatibility properties for direct object initialization:
    public string SourcePath
    {
        get => SourcePaths.Length > 0 ? SourcePaths[0] : string.Empty;
        init => SourcePaths = string.IsNullOrEmpty(value) ? [] : [value];
    }

    public string? DestinationPath
    {
        get => OutputPath;
        init => OutputPath = value;
    }

    [CommandOption("-l|--level <LEVEL>")]
    [Description("Compression level (0-9 for .zip where 0 = Store, 1-22 for .zrus). Default: 9.")]
    public int? Level { get; init; }

    [CommandOption("-p|--profile <PROFILE>")]
    [Description("Compression profile for .zrus: fast (3), balanced (9), high (15), ultra (22).")]
    public string? Profile { get; init; }

    [CommandOption("-a|--append")]
    [Description("Append sources to an existing archive instead of overwriting.")]
    public bool Append { get; init; }

    [CommandOption("-u|--update-only")]
    [Description("When appending, only replace existing entries if the source file is strictly newer.")]
    public bool UpdateOnly { get; init; }

    [CommandOption("--password <PWD>")]
    [Description("Password for encrypting the archive.")]
    public string? Password { get; init; }

    [CommandOption("-s|--split|--split-size <SIZE>")]
    [Description("Split archive into volumes of specified size (e.g. 100MB, 1GB, 4GB). Minimum: 64KB.")]
    public string? SplitSize { get; init; }
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
                if (settings.SourcePaths is null or { Length: 0 })
                {
                    throw new ArgumentException("At least one source path must be specified.");
                }

                string destination;
                IReadOnlyList<string> sourceArgs;

                if (!string.IsNullOrWhiteSpace(settings.OutputPath))
                {
                    destination = settings.OutputPath;
                    sourceArgs = settings.SourcePaths;
                }
                else if (settings.SourcePaths.Length == 1)
                {
                    var single = settings.SourcePaths[0];
                    if (string.IsNullOrWhiteSpace(single))
                    {
                        throw new ArgumentException("Source path cannot be empty.");
                    }
                    sourceArgs = [single];
                    destination = single + ".zrus";
                }
                else if (settings.SourcePaths.Length == 2)
                {
                    destination = settings.SourcePaths[1];
                    sourceArgs = [settings.SourcePaths[0]];
                }
                else
                {
                    var lastArg = settings.SourcePaths[^1];
                    var isLastCompressible = ArchiveFormatRegistry.TryDetect(lastArg, out var detected) && detected.CanCompress;
                    if (isLastCompressible)
                    {
                        destination = lastArg;
                        sourceArgs = settings.SourcePaths[..^1];
                    }
                    else
                    {
                        throw new ArgumentException(
                            "When specifying multiple source paths, the destination archive path must be specified via -o/--output or as the last argument ending in .zrus or .zip.");
                    }
                }

                if (sourceArgs.Count == 0)
                {
                    throw new ArgumentException("At least one source path must be specified.");
                }

                var resolvedSources = new List<string>();
                foreach (var s in sourceArgs)
                {
                    if (string.IsNullOrWhiteSpace(s))
                    {
                        throw new ArgumentException("Source path cannot be empty.");
                    }

                    var fullSource = Path.GetFullPath(s);
                    if (!File.Exists(fullSource) && !Directory.Exists(fullSource))
                    {
                        throw new FileNotFoundException($"Source path '{s}' does not exist.", fullSource);
                    }
                    resolvedSources.Add(fullSource);
                }

                if (!string.IsNullOrWhiteSpace(settings.Profile))
                {
                    var normalizedProfile = settings.Profile.Trim().ToLowerInvariant();
                    if (normalizedProfile is not ("fast" or "balanced" or "high" or "ultra"))
                    {
                        throw new ArgumentException($"Invalid compression profile '{settings.Profile}'. Valid profiles: fast, balanced, high, ultra.");
                    }
                }

                destination = Path.GetFullPath(destination);

                var formatDescriptor = ArchiveFormatRegistry.Detect(destination);
                if (!formatDescriptor.CanCompress)
                {
                    if (settings.Append)
                    {
                        throw new NotSupportedException($"Appending to archive format '{formatDescriptor.Format}' is not supported.");
                    }
                    var supportedCreationFormats = string.Join(", ", ArchiveFormatRegistry.CompressibleFormats.Select(f => f.PrimaryExtension));
                    throw new NotSupportedException($"Creation of archive format '{formatDescriptor.Format}' is not supported. Supported creation formats: {supportedCreationFormats}");
                }

                if (formatDescriptor.Format == ArchiveFormat.Zst)
                {
                    if (settings.Append)
                    {
                        throw new NotSupportedException("Appending is not supported for single-file streams.");
                    }

                    if (sourceArgs.Count != 1)
                    {
                        throw new ArgumentException("Single-file Zstandard compression (.zst) requires exactly one source file.");
                    }

                    if (Directory.Exists(resolvedSources[0]))
                    {
                        throw new ArgumentException("Single-file Zstandard compression (.zst) does not support directory input.");
                    }
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

                string formatStr = formatDescriptor.PrimaryExtension.TrimStart('.');
                var stopwatch = Stopwatch.StartNew();

                if (settings.Append)
                {
                    var appendRequest = new ArchiveAppendRequest(destination, sourceArgs, compressionLevel, settings.UpdateOnly, Password: settings.Password);
                    var appendResult = await _engine.AppendAsync(appendRequest, progress, ct);
                    stopwatch.Stop();

                    return new CompressResult(
                        Success: true,
                        SourcePaths: resolvedSources,
                        ArchivePath: destination,
                        Format: formatStr,
                        TotalFiles: appendResult.TotalFiles,
                        UncompressedBytes: appendResult.UncompressedBytes,
                        CompressedBytes: appendResult.CompressedBytes,
                        CompressionRatio: appendResult.CompressionRatio,
                        ElapsedMilliseconds: stopwatch.ElapsedMilliseconds,
                        SourcePath: string.Join(", ", resolvedSources)
                    );
                }

                long? splitSizeBytes = null;
                if (!string.IsNullOrWhiteSpace(settings.SplitSize))
                {
                    if (!DataSizeParser.TryParse(settings.SplitSize, out var parsedSize))
                    {
                        throw new ArgumentException($"Invalid split size '{settings.SplitSize}'. Use bytes or human units (e.g. 100MB, 1GB).");
                    }
                    if (parsedSize < DataSizeParser.MinimumSplitSizeBytes)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(settings.SplitSize),
                            parsedSize,
                            $"Split volume size must be at least {DataSizeParser.MinimumSplitSizeBytes:N0} bytes (64 KB).");
                    }
                    splitSizeBytes = parsedSize;
                }

                var request = new ArchiveCompressionRequest(sourceArgs, destination, compressionLevel, Password: settings.Password, SplitSizeBytes: splitSizeBytes);

                await _engine.CompressAsync(request, progress, ct);

                var volumeParts = await _engine.GetVolumePartsAsync(destination, ct);
                long totalCompressedBytes = 0;
                foreach (var part in volumeParts)
                {
                    if (File.Exists(part))
                    {
                        totalCompressedBytes += new FileInfo(part).Length;
                    }
                }

                long uncompressedSize = 0;
                int fileCount = 0;

                foreach (var source in resolvedSources)
                {
                    if (Directory.Exists(source))
                    {
                        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
                        fileCount += files.Length;
                        uncompressedSize += files.Sum(f => new FileInfo(f).Length);
                    }
                    else
                    {
                        fileCount += 1;
                        uncompressedSize += new FileInfo(source).Length;
                    }
                }

                double ratio = uncompressedSize > 0 ? (double)totalCompressedBytes / uncompressedSize : 1.0;
                formatStr = formatDescriptor.PrimaryExtension.TrimStart('.');
                stopwatch.Stop();

                var actualArchivePath = volumeParts.Count > 0 ? volumeParts[0] : destination;

                return new CompressResult(
                    Success: true,
                    SourcePaths: resolvedSources,
                    ArchivePath: actualArchivePath,
                    Format: formatStr,
                    TotalFiles: fileCount,
                    UncompressedBytes: uncompressedSize,
                    CompressedBytes: totalCompressedBytes,
                    CompressionRatio: Math.Round(ratio, 4),
                    ElapsedMilliseconds: stopwatch.ElapsedMilliseconds,
                    SourcePath: string.Join(", ", resolvedSources)
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


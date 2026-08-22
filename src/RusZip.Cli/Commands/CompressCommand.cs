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
    [CommandArgument(0, "<SOURCE>")]
    [Description("File or directory to compress.")]
    public string SourcePath { get; init; } = string.Empty;

    [CommandArgument(1, "[DESTINATION]")]
    [Description("Destination archive path (defaults to <SOURCE>.zrus).")]
    public string? DestinationPath { get; init; }

    [CommandOption("-l|--level <LEVEL>")]
    [Description("Compression level (1-22 for .zrus, 1-9 for .zip). Default: 9.")]
    public int? Level { get; init; }

    [CommandOption("-p|--profile <PROFILE>")]
    [Description("Compression profile: fast (3), balanced (9), high (15), ultra (22).")]
    public string? Profile { get; init; }
}

public sealed class CompressCommand(IArchiveEngine engine) : AsyncCommand<CompressSettings>
{
    private readonly IArchiveEngine _engine = engine;

    public override async Task<int> ExecuteAsync(CommandContext context, CompressSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SourcePath))
        {
            if (settings.Json)
                CliJsonSerializer.EmitError("ARGUMENT_ERROR", "Source path cannot be empty.");
            else
                AnsiConsole.MarkupLine("[red]Error:[/] Source path cannot be empty.");
            return 2;
        }

        var source = Path.GetFullPath(settings.SourcePath);
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            if (settings.Json)
                CliJsonSerializer.EmitError("SOURCE_NOT_FOUND", $"Source path '{settings.SourcePath}' does not exist.");
            else
                AnsiConsole.MarkupLine($"[red]Error:[/] Source path '{Markup.Escape(settings.SourcePath)}' does not exist.");
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(settings.Profile))
        {
            var normalizedProfile = settings.Profile.Trim().ToLowerInvariant();
            if (normalizedProfile is not ("fast" or "balanced" or "high" or "ultra"))
            {
                if (settings.Json)
                    CliJsonSerializer.EmitError("ARGUMENT_ERROR", $"Invalid compression profile '{settings.Profile}'. Valid profiles: fast, balanced, high, ultra.");
                else
                    AnsiConsole.MarkupLine($"[red]Error:[/] Invalid compression profile '{Markup.Escape(settings.Profile)}'. Valid profiles: fast, balanced, high, ultra.");
                return 2;
            }
        }

        if (settings.Level.HasValue)
        {
            if (settings.Level.Value < CompressionProfiles.MinLevel || settings.Level.Value > CompressionProfiles.MaxLevel)
            {
                if (settings.Json)
                    CliJsonSerializer.EmitError("ARGUMENT_ERROR", $"Compression level must be between {CompressionProfiles.MinLevel} and {CompressionProfiles.MaxLevel}.");
                else
                    AnsiConsole.MarkupLine($"[red]Error:[/] Compression level must be between {CompressionProfiles.MinLevel} and {CompressionProfiles.MaxLevel}.");
                return 2;
            }
        }

        var destination = settings.DestinationPath ?? (source + ".zrus");
        destination = Path.GetFullPath(destination);

        ArchiveFormatDescriptor formatDescriptor;
        try
        {
            formatDescriptor = ArchiveFormatRegistry.Detect(destination);
            if (!formatDescriptor.CanCompress)
            {
                var supportedCreationFormats = string.Join(", ", ArchiveFormatRegistry.CompressibleFormats.Select(f => f.PrimaryExtension));
                var errorMsg = $"Creation of archive format '{formatDescriptor.Format}' is not supported. Supported creation formats: {supportedCreationFormats}";
                if (settings.Json)
                    CliJsonSerializer.EmitError("UNSUPPORTED_FORMAT", errorMsg);
                else
                    AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(errorMsg)}");
                return 2;
            }
        }
        catch (NotSupportedException ex)
        {
            if (settings.Json)
                CliJsonSerializer.EmitError("UNSUPPORTED_FORMAT", ex.Message);
            else
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }

        var compressionLevel = CompressionProfiles.ResolveLevel(settings.Profile, settings.Level);
        var request = new ArchiveCompressionRequest(source, destination, compressionLevel);

        var sw = Stopwatch.StartNew();

        try
        {
            await CliProgressBridge.ExecuteWithProgressAsync(
                "Compressing",
                settings.Json,
                async (prog, ct) => await _engine.CompressAsync(request, prog, ct)
            );

            sw.Stop();

            var destInfo = new FileInfo(destination);
            long uncompressedSize = Directory.Exists(source)
                ? Directory.GetFiles(source, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
                : new FileInfo(source).Length;
            int fileCount = Directory.Exists(source)
                ? Directory.GetFiles(source, "*", SearchOption.AllDirectories).Length
                : 1;

            double ratio = uncompressedSize > 0 ? (double)destInfo.Length / uncompressedSize : 1.0;

            string formatStr = formatDescriptor.PrimaryExtension.TrimStart('.');

            if (settings.Json)
            {
                CliJsonSerializer.Emit(new CompressResult(
                    Success: true,
                    SourcePath: source,
                    ArchivePath: destination,
                    Format: formatStr,
                    TotalFiles: fileCount,
                    UncompressedBytes: uncompressedSize,
                    CompressedBytes: destInfo.Length,
                    CompressionRatio: Math.Round(ratio, 4),
                    ElapsedMilliseconds: sw.ElapsedMilliseconds
                ));
            }
            else
            {
                var summaryTable = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Metric")
                    .AddColumn("Value")
                    .AddRow("Archive Path", Markup.Escape(destination))
                    .AddRow("Total Files", fileCount.ToString("N0"))
                    .AddRow("Uncompressed Size", CliProgressBridge.FormatBytes(uncompressedSize))
                    .AddRow("Compressed Size", CliProgressBridge.FormatBytes(destInfo.Length))
                    .AddRow("Ratio", $"{ratio * 100:N1}%")
                    .AddRow("Time Elapsed", $"{sw.ElapsedMilliseconds} ms");

                AnsiConsole.Write(new Panel(summaryTable).Header("[bold green]Compression Summary[/]"));
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (settings.Json)
                CliJsonSerializer.EmitError("COMPRESS_FAILED", ex.Message, ex.StackTrace);
            else
                AnsiConsole.WriteException(ex);
            return 1;
        }
    }
}

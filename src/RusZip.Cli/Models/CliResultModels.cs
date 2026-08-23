using System.Text.Json;
using System.Text.Json.Serialization;

namespace RusZip.Cli.Models;

public static class CliJsonSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        // Strict/default escaping is used on purpose: '<', '>', '&' and control characters
        // are escaped, which keeps machine-parsed JSON safe to embed and re-render.
    };

    public static void Emit<T>(T value, TextWriter? writer = null) =>
        (writer ?? Console.Out).WriteLine(JsonSerializer.Serialize(value, Options));

    public static void EmitError(string code, string message, string? details = null, TextWriter? writer = null) =>
        (writer ?? Console.Out).WriteLine(JsonSerializer.Serialize(new ErrorResult(false, new ErrorDetail(code, message, details)), Options));
}

public sealed record CompressResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> SourcePaths { get; init; } = [];
    public string ArchivePath { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public int TotalFiles { get; init; }
    public long UncompressedBytes { get; init; }
    public long CompressedBytes { get; init; }
    public double CompressionRatio { get; init; }
    public double ElapsedMilliseconds { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourcePath { get; init; }

    [JsonConstructor]
    public CompressResult(
        bool Success,
        IReadOnlyList<string> SourcePaths,
        string ArchivePath,
        string Format,
        int TotalFiles,
        long UncompressedBytes,
        long CompressedBytes,
        double CompressionRatio,
        double ElapsedMilliseconds,
        string? SourcePath = null)
    {
        this.Success = Success;
        this.SourcePaths = SourcePaths ?? (SourcePath != null ? [SourcePath] : []);
        this.ArchivePath = ArchivePath;
        this.Format = Format;
        this.TotalFiles = TotalFiles;
        this.UncompressedBytes = UncompressedBytes;
        this.CompressedBytes = CompressedBytes;
        this.CompressionRatio = CompressionRatio;
        this.ElapsedMilliseconds = ElapsedMilliseconds;
        this.SourcePath = SourcePath ?? (this.SourcePaths.Count == 1 ? this.SourcePaths[0] : null);
    }

    public CompressResult(
        bool Success,
        string SourcePath,
        string ArchivePath,
        string Format,
        int TotalFiles,
        long UncompressedBytes,
        long CompressedBytes,
        double CompressionRatio,
        double ElapsedMilliseconds)
        : this(Success, [SourcePath], ArchivePath, Format, TotalFiles, UncompressedBytes, CompressedBytes, CompressionRatio, ElapsedMilliseconds, SourcePath)
    {
    }
}



public sealed record ExtractResult(
    bool Success,
    string ArchivePath,
    string DestinationPath,
    int ExtractedFiles,
    long TotalBytes,
    double ElapsedMilliseconds
);

public sealed record ListEntryItem(
    string Path,
    bool IsDirectory,
    long UncompressedSize,
    long? CompressedSize,
    DateTimeOffset? LastModified
);

public sealed record ListResult(
    bool Success,
    string ArchivePath,
    string Format,
    int TotalEntries,
    long TotalUncompressedBytes,
    IReadOnlyList<ListEntryItem> Entries
);

public sealed record ErrorDetail(string Code, string Message, string? Details = null);
public sealed record ErrorResult(bool Success, ErrorDetail Error);

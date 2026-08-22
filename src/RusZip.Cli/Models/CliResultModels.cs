using System.Text.Json;
using System.Text.Json.Serialization;

namespace RusZip.Cli.Models;

public static class CliJsonSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Emit<T>(T value) =>
        Console.Out.WriteLine(JsonSerializer.Serialize(value, Options));

    public static void EmitError(string code, string message, string? details = null) =>
        Console.Out.WriteLine(JsonSerializer.Serialize(new ErrorResult(false, new ErrorDetail(code, message, details)), Options));
}

public sealed record CompressResult(
    bool Success,
    string SourcePath,
    string ArchivePath,
    string Format,
    int TotalFiles,
    long UncompressedBytes,
    long CompressedBytes,
    double CompressionRatio,
    double ElapsedMilliseconds
);

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

using System.Diagnostics.CodeAnalysis;

namespace RusZip.Core.Models;

public static class ArchiveFormatRegistry
{
    public static readonly ArchiveFormatDescriptor Zrus = new(
        Format: ArchiveFormat.Zrus,
        DisplayName: "Zstandard TAR Archive (.zrus)",
        PrimaryExtension: ".zrus",
        Extensions: [".zrus"],
        CanCompress: true,
        CanDecompress: true,
        MinCompressionLevel: 1,
        MaxCompressionLevel: 22,
        DefaultCompressionLevel: 9,
        MimeType: "application/x-zstd-tar",
        CategoryDescription: "High-performance POSIX Tar with Zstandard streaming compression"
    );

    public static readonly ArchiveFormatDescriptor Zip = new(
        Format: ArchiveFormat.Zip,
        DisplayName: "Standard Zip Archive (.zip)",
        PrimaryExtension: ".zip",
        Extensions: [".zip"],
        CanCompress: true,
        CanDecompress: true,
        MinCompressionLevel: 0,
        MaxCompressionLevel: 9,
        DefaultCompressionLevel: 6,
        MimeType: "application/zip",
        CategoryDescription: "Universal cross-platform standard Zip format"
    );

    public static readonly ArchiveFormatDescriptor TarGz = new(
        Format: ArchiveFormat.TarGz,
        DisplayName: "GZip Compressed Tar (.tar.gz, .tgz)",
        PrimaryExtension: ".tar.gz",
        Extensions: [".tar.gz", ".tgz"],
        CanCompress: false,
        CanDecompress: true,
        MinCompressionLevel: 0,
        MaxCompressionLevel: 0,
        DefaultCompressionLevel: 0,
        MimeType: "application/gzip",
        CategoryDescription: "Tar archive compressed with GZip"
    );

    public static readonly ArchiveFormatDescriptor SevenZip = new(
        Format: ArchiveFormat.SevenZip,
        DisplayName: "7-Zip Archive (.7z)",
        PrimaryExtension: ".7z",
        Extensions: [".7z"],
        CanCompress: false,
        CanDecompress: true,
        MinCompressionLevel: 0,
        MaxCompressionLevel: 0,
        DefaultCompressionLevel: 0,
        MimeType: "application/x-7z-compressed",
        CategoryDescription: "7-Zip LZMA container"
    );

    public static readonly ArchiveFormatDescriptor Rar = new(
        Format: ArchiveFormat.Rar,
        DisplayName: "RAR Archive (.rar)",
        PrimaryExtension: ".rar",
        Extensions: [".rar"],
        CanCompress: false,
        CanDecompress: true,
        MinCompressionLevel: 0,
        MaxCompressionLevel: 0,
        DefaultCompressionLevel: 0,
        MimeType: "application/vnd.rar",
        CategoryDescription: "RAR4/RAR5 container"
    );

    public static readonly ArchiveFormatDescriptor Gz = new(
        Format: ArchiveFormat.Gz,
        DisplayName: "GZip Compressed File (.gz)",
        PrimaryExtension: ".gz",
        Extensions: [".gz"],
        CanCompress: false,
        CanDecompress: true,
        MinCompressionLevel: 0,
        MaxCompressionLevel: 0,
        DefaultCompressionLevel: 0,
        MimeType: "application/gzip",
        CategoryDescription: "Single file GZip compressed stream"
    );

    private static readonly IReadOnlyList<ArchiveFormatDescriptor> AllFormats =
    [
        TarGz,   // Checked before Gz / Tar
        Zrus,
        Zip,
        SevenZip,
        Rar,
        Gz
    ];

    public static IReadOnlyList<ArchiveFormatDescriptor> Formats => AllFormats;

    public static IReadOnlyList<ArchiveFormatDescriptor> CompressibleFormats { get; } =
        AllFormats.Where(f => f.CanCompress).ToList();

    public static IReadOnlyList<ArchiveFormatDescriptor> DecompressibleFormats { get; } =
        AllFormats.Where(f => f.CanDecompress).ToList();

    public static IReadOnlyCollection<string> SupportedExtensions { get; } =
        AllFormats.SelectMany(f => f.Extensions).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool TryDetect(string? pathOrExtension, [NotNullWhen(true)] out ArchiveFormatDescriptor? descriptor)
    {
        if (string.IsNullOrWhiteSpace(pathOrExtension))
        {
            descriptor = null;
            return false;
        }

        var normalized = pathOrExtension.Trim();

        foreach (var candidate in AllFormats)
        {
            foreach (var ext in candidate.Extensions)
            {
                if (normalized.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ||
                    (!normalized.Contains('/') && !normalized.Contains('\\') && ext.TrimStart('.').Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    descriptor = candidate;
                    return true;
                }
            }
        }

        descriptor = null;
        return false;
    }

    public static ArchiveFormatDescriptor Detect(string pathOrExtension)
    {
        if (TryDetect(pathOrExtension, out var descriptor))
        {
            return descriptor;
        }

        throw new NotSupportedException(
            $"Unsupported archive format for '{pathOrExtension}'. Supported formats: {string.Join(", ", SupportedExtensions)}");
    }

    public static bool IsSupportedArchive(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && TryDetect(path, out _);
    }
}

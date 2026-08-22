namespace RusZip.Core.Models;

public sealed record ArchiveEntry(
    string RelativePath,
    long UncompressedSize,
    long? CompressedSize,
    DateTimeOffset? LastModified,
    bool IsDirectory,
    bool IsEncrypted = false,
    string Attributes = ""
);

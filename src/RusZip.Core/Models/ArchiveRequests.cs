namespace RusZip.Core.Models;

public sealed record ArchiveCompressionRequest(
    string SourcePath,
    string DestinationArchivePath,
    int CompressionLevel = 9
);

public sealed record ArchiveExtractionRequest(
    string ArchivePath,
    string DestinationDirectory,
    bool Overwrite = true
);

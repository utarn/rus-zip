namespace RusZip.Core.Models;

public sealed record ArchiveCompressionRequest(
    string SourcePath,
    string DestinationArchivePath,
    int CompressionLevel = 9
);

public sealed record ArchiveExtractionRequest(
    string ArchivePath,
    string DestinationDirectory,
    bool Overwrite = true,
    ExtractionLimits? Limits = null
);

/// <summary>
/// Hard-fail guardrails for extracting untrusted archives. When a limit is <see langword="null"/>
/// or less than or equal to zero the corresponding dimension is unlimited. A <see langword="null"/>
/// <see cref="ArchiveExtractionRequest.Limits"/> on the request means the
/// <see cref="Engines.SafeArchiveExtractor"/> defaults apply (64 GB cumulative output, 1,000,000 entries).
/// Limits are measured from actual streamed bytes and processed entries — never header metadata.
/// </summary>
public sealed record ExtractionLimits(
    long? MaxCumulativeUncompressedBytes,
    int? MaxEntryCount
);

/// <summary>
/// Actual (post-extraction) accounting returned by the engine, measured from real streamed
/// bytes and processed entries rather than archive header metadata.
/// </summary>
public sealed record ExtractionResult(
    long BytesExtracted,
    int FilesExtracted,
    int EntriesProcessed
);

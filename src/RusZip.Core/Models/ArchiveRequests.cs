namespace RusZip.Core.Models;

public sealed record ArchiveCompressionRequest(
    IReadOnlyList<string> SourcePaths,
    string DestinationArchivePath,
    int CompressionLevel = 9,
    string? BaseDirectory = null
)
{
    public ArchiveCompressionRequest(string sourcePath, string destinationArchivePath, int compressionLevel = 9)
        : this([sourcePath], destinationArchivePath, compressionLevel) { }

    public string SourcePath => SourcePaths.Count > 0 ? SourcePaths[0] : string.Empty;
}


/// <summary>
/// Request to extract an archive.
/// </summary>
/// <param name="ArchivePath">Path to the archive file.</param>
/// <param name="DestinationDirectory">Directory to extract into.</param>
/// <param name="Overwrite">Whether to overwrite existing files at the destination.</param>
/// <param name="Limits">Extraction guardrails (see <see cref="ExtractionLimits"/>).</param>
/// <param name="Entries">
/// Optional selective-extraction filter: the set of relative entry paths to extract. When
/// <see langword="null"/> or empty, all entries are extracted (existing behavior). When set, only
/// entries matching a filter path are extracted. Matching is exact relative-path equality OR
/// directory-prefix match: an entry is extracted when its normalized relative path equals a filter
/// path, or begins with a filter path followed by a <c>'/'</c> (so a directory filter extracts that
/// directory's subtree). Both the entry path and each filter path are normalized before matching:
/// <c>'\'</c> is replaced with <c>'/'</c> and leading/trailing <c>'/'</c> are trimmed, making a
/// trailing separator on a directory filter optional. If the filter is set but no archive entry
/// matches, extraction fails with an <see cref="InvalidOperationException"/> (see
/// <c>RusZip.Core.Engines.EntryFilter</c>).
/// </param>
public sealed record ArchiveExtractionRequest(
    string ArchivePath,
    string DestinationDirectory,
    bool Overwrite = true,
    ExtractionLimits? Limits = null,
    IReadOnlyList<string>? Entries = null
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

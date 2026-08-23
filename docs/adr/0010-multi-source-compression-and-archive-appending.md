# 0010: Multi-Source Compression and Atomic Archive Appending

We decided to support multi-source inputs for archive compression and an atomic rewrite pipeline for appending entries to `.zrus` and `.zip` archives.

## Context
`rus-zip` CLI previously only supported a single source input (`<SOURCE> [DESTINATION]`) when creating archives and did not support updating or adding entries to existing archives. Users need to bundle multiple distinct files, folders, or globbed paths into a single archive, and append new or modified files incrementally without manual decompression cycles.

## Decision
1. **Multi-Source CLI Interface**:
   - `rus-zip compress` accepts multiple positional sources and an optional destination path via `-o|--output <PATH>` or as the last positional argument when matching a known compressible format extension (`.zrus`, `.zip`). Single-source syntax (`rus-zip c <SOURCE>`) remains backwards-compatible, defaulting to `<SOURCE>.zrus`.
   - Entry relative paths preserve typed relative subpaths or basenames while sanitizing path traversal segments (`..`).
   - All sources are validated upfront before any archive write begins (atomic fail-fast).
2. **Dedicated Append Subcommand & Flag**:
   - Add `rus-zip append` (aliases: `a`, `add`) accepting `<ARCHIVE> <SOURCES...>`.
   - Add `-a|--append` flag to `rus-zip compress`.
   - Add `-u|--update-only` flag to only replace existing entries if the source file timestamp is strictly newer.
3. **Atomic Safe Rewrite Pipeline**:
   - For `.zrus` (Tar + Zstandard) and `.zip` archives, appending unpacks/reads existing entries and streams both preserved and new entries into a temporary file (`<ARCHIVE>.tmp.<GUID>`), replacing the target archive on completion.
   - Non-compressible/read-only formats (`.rar`, `.7z`, `.gz`, `.tar.gz`) reject append operations with a descriptive `NotSupportedException`.
4. **Engine Abstraction**:
   - Extend `ArchiveCompressionRequest` with `IReadOnlyList<string> SourcePaths`.
   - Add `ArchiveAppendRequest` and `IArchiveEngine.AppendAsync(...)` to `RusZip.Core`.
   - Progress reporting calculates cumulative uncompressed bytes across retained and new entries for smooth, continuous metrics.

## Technical Architecture & Code Contracts

### 1. Request Models in `RusZip.Core.Models`
```csharp
// In src/RusZip.Core/Models/ArchiveRequests.cs
public sealed record ArchiveCompressionRequest(
    IReadOnlyList<string> SourcePaths,
    string DestinationArchivePath,
    int CompressionLevel = 9,
    string? BaseDirectory = null
)
{
    public ArchiveCompressionRequest(string sourcePath, string destinationArchivePath, int compressionLevel = 9)
        : this([sourcePath], destinationArchivePath, compressionLevel) { }
}

public sealed record ArchiveAppendRequest(
    string ArchivePath,
    IReadOnlyList<string> SourcePaths,
    int CompressionLevel = 9,
    bool UpdateOnly = false,
    string? BaseDirectory = null
);

public sealed record AppendResult(
    bool Success,
    string ArchivePath,
    string Format,
    int AddedFiles,
    int UpdatedFiles,
    int RetainedFiles,
    int SkippedFiles,
    int TotalFiles,
    long UncompressedBytes,
    long CompressedBytes,
    double CompressionRatio,
    long ElapsedMilliseconds
);
```

### 2. `IArchiveEngine` Interface in `RusZip.Core.Abstractions`
```csharp
// In src/RusZip.Core/Abstractions/IArchiveEngine.cs
public interface IArchiveEngine
{
    Task CompressAsync(
        ArchiveCompressionRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default);

    Task<AppendResult> AppendAsync(
        ArchiveAppendRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default);

    Task<ExtractionResult> ExtractAsync(
        ArchiveExtractionRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        CancellationToken ct = default);
}
```

## Consequences
- Enables rich multi-file bundling and incremental packaging across `.zrus` and `.zip`.
- Atomic temporary file writes guarantee the original archive is never corrupted if the operation is cancelled or errors mid-stream.
- Fully compatible with existing CLI scripts and JSON automation modes.

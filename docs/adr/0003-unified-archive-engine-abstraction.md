# 0003: Unified Archive Engine Abstraction

We decided to use pure managed/P-Invoke cross-platform libraries (`ZstdSharp.Port` and `SharpCompress`) behind a unified `IArchiveEngine` domain abstraction.

## Context
`rus-zip` must handle compression/decompression for `.zrus` and `.zip`, and decompression for `.rar`, `.7z`, `.gz`, and `.tar.gz` across Windows, Linux, and macOS without requiring external native tools or CLI binaries to be pre-installed on the host system.

## Decision
1. Implement `IArchiveEngine` in `RusZip.Core` providing:
   - `Task CompressAsync(ArchiveCompressionRequest request, IProgress<ProgressReport>? progress, CancellationToken ct)`
   - `Task ExtractAsync(ArchiveExtractionRequest request, IProgress<ProgressReport>? progress, CancellationToken ct)`
   - `Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, CancellationToken ct)`
2. Use `ZstdSharp.Port` for Zstandard compression/decompression streams integrated with Tar archive writers/readers.
3. Use `SharpCompress` for multi-format decoding (`.zip`, `.rar`, `.7z`, `.gz`, `.tar.gz`) and standard zip creation.

## Consequences
Guarantees identical behavior and full functionality out of the box across all supported operating systems without host dependencies.

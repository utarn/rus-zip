# 0004: Safe Archive Extraction Pipeline

We decided to consolidate all archive extraction and stream consumption across all formats into a single deep `SafeArchiveExtractor` module in `RusZip.Core`.

## Context
Previously, extraction logic was duplicated across `ZstdTarArchiveEngine`, `SharpCompressArchiveEngine` (standard formats), custom GZip streams, and TarGz streams. This duplication led to subtle security disparities (e.g. inconsistent Zip-Slip path traversal checks), divergent buffer allocation strategies, and fragile directory timestamp restoration when child files were written.

## Decision
1. Introduce `IArchiveExtractionSource` and `ExtractionEntry` in `RusZip.Core.Engines` as the unified streaming contract for archive decoders.
2. Centralize path traversal defenses in `SafeArchiveExtractor.ExtractAllAsync`, verifying destination root prefixes and rejecting malicious rooted/relative paths with `SecurityException`.
3. Use `ArrayPool<byte>.Shared` 80 KB rented buffers for zero-allocation streaming.
4. Execute directory timestamp and POSIX `UnixFileMode` restoration in a second pass in bottom-up (deepest directory first) order.

## Consequences
Eliminates code duplication across all archive extraction engines, prevents path traversal vulnerabilities across all formats, and ensures accurate POSIX directory timestamps.

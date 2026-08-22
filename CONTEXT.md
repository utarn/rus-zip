# Context: rus-zip

Glossary and ubiquitous language for `rus-zip`.

## Glossary

### rus-zip
A cross-platform desktop application and command-line tool for compressing and decompressing archive files across Windows, Linux, and macOS.

### .zrus
The default archive format of `rus-zip`. Combines a Tar container structure (preserving multi-file directory trees, POSIX permissions, and timestamps) with Zstandard (`zstd`) compression, supporting configurable compression levels (1–22).

### Core Engine
The headless library (`RusZip.Core`) providing unified archive abstraction, stream compression, decompression, entry inspection, and progress reporting without UI or CLI dependencies.

### Archive Format Registry
The domain registry in `RusZip.Core` (`ArchiveFormatRegistry` and `ArchiveFormatDescriptor`) providing the canonical definitions of all archive formats, extension aliases (`.tar.gz`, `.tgz`), bidirectional capabilities (`CanCompress`, `CanDecompress`), compression level ranges, and MIME types.

### Safe Archive Extractor
The centralized stream-consumer extraction module in `RusZip.Core` (`SafeArchiveExtractor` and `IArchiveExtractionSource`) enforcing path traversal defenses, buffer-pooled streaming, progress reporting, and two-pass bottom-up directory timestamp and POSIX mode restoration.

### Data Metrics Formatter & Throughput Tracker
Core modules (`DataMetricsFormatter` and `ThroughputTracker`) that standardize byte size formatting (`KB`, `MB`, `GB`), compression ratio calculation, exponential moving average (EMA) speed smoothing, and dynamic ETA estimation across CLI and Desktop.

### Archive Hierarchy
The headless tree projection module in `RusZip.Core` (`ArchiveHierarchy` and `ArchiveTreeNode`) that converts flat `ArchiveEntry` lists into recursive directory/file trees with automated size rollups, without UI framework dependencies.

### CLI Command Runner
The infrastructure seam in `RusZip.Cli` (`CliCommandRunner`) that standardizes execution lifecycles, stopwatch timing, progress bridge management, dual JSON/console output, and unified exception-to-exit-code translation.

### Supported Formats
The archive formats handled by the engine:
- **Bi-directional (Compress & Decompress)**: `.zrus` (Tar+Zstd), `.zip`.
- **Decompress Only**: `.rar`, `.7z`, `.gz`, `.tar.gz`.

### Archive Entry
A descriptor representing a single file or directory within an archive, encapsulating its relative path, uncompressed size, compressed size, last modified timestamp, and attributes.

### Compression Level
An integer value indicating the compression aggressiveness. For Zstandard (`.zrus`), spans levels 1 (fastest) through 22 (maximum ratio, ultra).

### Compression Profile
Standard preset configurations mapping friendly names to compression levels:
- **Fast**: Level 3 (high speed, lower CPU)
- **Balanced**: Level 9 (default general purpose)
- **High**: Level 15 (improved ratio)
- **Ultra**: Level 22 (maximum compression)

### Untrusted Archive
Any archive whose origin cannot be vouched for (downloaded, emailed, received). rus-zip treats every archive as untrusted: entry names and metadata are attacker-controlled and must never reach the filesystem or an output surface unsanitized.

### Extraction Guardrails
The safety limits enforced while extracting an untrusted archive: a cap on cumulative uncompressed output size and a cap on entry count. Exceeding either aborts the extraction with a clear error rather than warning; limits are user-configurable.

### Progress Report
A real-time progress model capturing processed bytes, total bytes, current file being processed, percentage completed, and cancellation state.

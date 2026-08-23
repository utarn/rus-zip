# Context: rus-zip

Glossary and ubiquitous language for `rus-zip`.

## Glossary

### rus-zip
A cross-platform desktop application and command-line tool for compressing and decompressing archive files across Windows, Linux, and macOS.

### .zrus
The default archive format of `rus-zip`. Combines a Tar container structure (preserving multi-file directory trees, POSIX permissions, and timestamps) with Zstandard (`zstd`) compression, supporting configurable compression levels (1–22). Recognized extension aliases include `.tar.zstd` and `.tzstd`.

### .zst
A single file compressed with Zstandard (`zstd`) streaming compression without a Tar container structure (analogous to `.gz`), supporting decompression across CLI and Desktop, and single-file CLI compression.

### Core Engine
The headless library (`RusZip.Core`) providing unified archive abstraction, stream compression, decompression, entry inspection, and progress reporting without UI or CLI dependencies.

### Archive Format Registry
The domain registry in `RusZip.Core` (`ArchiveFormatRegistry` and `ArchiveFormatDescriptor`) providing the canonical definitions of all archive formats, extension aliases (`.tar.gz`, `.tgz`, `.tar.zstd`, `.tzstd`), bidirectional capabilities (`CanCompress`, `CanDecompress`), compression level ranges, and MIME types.

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
- **Bi-directional (Compress & Decompress)**: `.zrus` (Tar+Zstd, aliases: `.tar.zstd`, `.tzstd`), `.zip`, `.zst` (single-file stream, CLI only for compression).
- **Decompress Only**: `.rar`, `.7z`, `.gz`, `.tar.gz` (alias: `.tgz`).
- **GUI Creation Restricted**: The Desktop GUI creation wizard restricts output format selection strictly to `.zrus` and `.zip`.

### Archive Entry
A descriptor representing a single file or directory within an archive, encapsulating its relative path, uncompressed size, compressed size, last modified timestamp, and attributes.

### Compression Level
An integer value indicating the compression aggressiveness. For Zstandard (`.zrus`, `.tar.zstd`, `.tzstd`, `.zst`), spans levels 1 (fastest) through 22 (maximum ratio, ultra).

### Compression Profile
Standard preset configurations mapping friendly names to compression levels:
- **Fast**: Level 3 (high speed, lower CPU)
- **Balanced**: Level 9 (default general purpose)
- **High**: Level 15 (improved ratio)
- **Ultra**: Level 22 (maximum compression)

### Untrusted Archive
Any archive whose origin cannot be vouched for (downloaded, emailed, received). rus-zip treats every archive as untrusted: entry names and metadata are attacker-controlled and must never reach the filesystem or an output surface unsanitized.

### Extraction Guardrails
The safety limits enforced while extracting an untrusted archive: a cap on cumulative uncompressed output size (default 64 GB) and a cap on entry count (default 1,000,000). Exceeding either aborts extraction with `ExtractionLimitExceededException` (mapped to exit 1, `EXECUTION_ERROR`) and best-effort cleanup of partial output. Limits are user-configurable via CLI flags (`--max-uncompressed-size`, `--max-entries`) or Desktop extraction settings, and are measured from actual streamed bytes and processed entries — never spoofable header metadata. A `0`/`null` limit means unlimited.

### Progress Report
A real-time progress model capturing processed bytes, total bytes, current file being processed, percentage completed, and cancellation state.

### File Association Service
The cross-platform service managing OS-level file extension registrations, ProgIDs, MIME types, and default application queries across Windows, Linux, and macOS.

### Shell Verbs
The OS shell context menu actions registered for supported archive formats: `Extract here`, `Extract to...`, and `Extract to subfolder`.

### Quick Extract
The lightweight, standalone extraction execution workflow displaying real-time progress, metrics, throughput, and completion actions without opening the full archive browser interface.

### File Conflict Resolver
The interactive or policy-driven callback mechanism in `RusZip.Core` resolving destination file collisions during extraction via explicit decisions (`Overwrite`, `OverwriteAll`, `Skip`, `SkipAll`, `Abort`).

### Compound Extension Stripping
The process of detecting and stripping multi-part extension aliases (e.g. `.tar.gz`, `.tgz`, `.tar.zstd`, `.tzstd`) based on the Archive Format Registry to determine canonical archive base names and prevent redundant nested directory names.

### Auto-Suffixed Extraction Directory
A collision-free destination directory generation strategy that appends an incremental numerical suffix (`_2`, `_3`, ...) when a target folder already exists at the extraction location.

### Single-Instance IPC Coordinator
The inter-process communication mechanism that forwards file opening requests to an existing running application window and brings it to the foreground.

### Multi-Source Packaging
The capability to bundle multiple distinct input files, directories, or globbed paths into a single `.zrus` or `.zip` archive, preserving relative paths or basenames while sanitizing path traversals (`..`).

### Archive Appending
The process of incrementally adding or updating entries in an existing `.zrus` or `.zip` archive using an atomic rewrite pipeline (`.tmp.uuid`), guaranteeing that interrupted operations leave the original archive intact.

### Entry Collision Policy
The rule determining how duplicate entry paths are resolved during an archive append operation: default replacement/overwriting of older entries, or conditional updating only when the incoming file has a strictly newer modification timestamp (`--update-only`).


# Context: rus-zip

Glossary and ubiquitous language for `rus-zip`.

## Glossary

### rus-zip
A cross-platform desktop application and command-line tool for compressing and decompressing archive files across Windows, Linux, and macOS.

### .zrus
The default archive format of `rus-zip`. Combines a Tar container structure (preserving multi-file directory trees, POSIX permissions, and timestamps) with Zstandard (`zstd`) compression, supporting configurable compression levels (1–22).

### Core Engine
The headless library (`RusZip.Core`) providing unified archive abstraction, stream compression, decompression, entry inspection, and progress reporting without UI or CLI dependencies.

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

### Progress Report
A real-time progress model capturing processed bytes, total bytes, current file being processed, percentage completed, and cancellation state.

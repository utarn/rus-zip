# 0001: .zrus Archive Container Specification

We decided to define the default `.zrus` archive container format as a Tar archive stream compressed with Zstandard (`zstd`).

## Context
`rus-zip` requires a high-performance default compression format (`.zrus`) utilizing the modern Zstandard algorithm across Windows, Linux, and macOS. While Zstandard compresses single data streams, multi-file archives require container semantics (directory trees, timestamps, permissions, entry attributes).

## Decision
1. `.zrus` archives will encapsulate a standard POSIX/GNU Tar structure compressed via a Zstandard streaming frame (`.tar.zst` payload format).
2. The format supports compression levels 1 through 22 (with default level 9).
3. The format supports streaming decompression and entry indexing without full uncompressed disk caching.

## Considered Options
- **Custom Binary Container**: Rejected for MVP due to high complexity and redundancy with existing robust POSIX tar standards.
- **Zip Container with Zstd Method**: Rejected due to partial toolchain support and zip legacy quirks compared to pure tar+zstd streams.

## Consequences
Enables immediate cross-platform compatibility, directory preservation, and efficient streaming compression/decompression.

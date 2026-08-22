# 0005: Centralized Archive Format Registry and Capability Model

We decided to replace scattered format enums, string arrays, and hardcoded extension checks with a centralized `ArchiveFormatRegistry` in `RusZip.Core`.

## Context
Format extensions (`.zrus`, `.zip`, `.tar.gz`, `.tgz`, `.7z`, `.rar`, `.gz`), compression capabilities (`CanCompress`, `CanDecompress`), and compression level bounds were duplicated across `ArchiveFormatDetector`, `UnifiedArchiveEngine`, CLI command settings, and Avalonia view models (`MainWindowViewModel`, `CompressionSettingsViewModel`).

## Decision
1. Define `ArchiveFormatDescriptor` encapsulating `Format`, `DisplayName`, `PrimaryExtension`, `Extensions` collection, `CanCompress`, `CanDecompress`, `MinCompressionLevel`, `MaxCompressionLevel`, `DefaultCompressionLevel`, `MimeType`, and `CategoryDescription`.
2. Introduce `ArchiveFormatRegistry` providing fast case-insensitive detection (`Detect`, `TryDetect`), capability queries (`CompressibleFormats`, `DecompressibleFormats`), and `IsSupportedArchive(path)`.
3. Desktop ViewModels and CLI commands delegate all extension checking and format dropdown population to `ArchiveFormatRegistry`.

## Consequences
Single source of truth for format rules and capability limits. Adding support for future formats requires changes only to `ArchiveFormatRegistry`.

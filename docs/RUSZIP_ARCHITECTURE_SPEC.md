# RusZip 2.0 Architectural Deepening Specification

This specification documents the deepened architecture and modular seams designed to eliminate code duplication, centralize domain invariants, and cleanly separate domain models from UI and CLI presentation adapters.

---

## 1. Executive Summary & Design Principles

Following the deep module philosophy (`/codebase-design`):
- **Deep Modules**: Modules provide powerful functionality behind small, simple interfaces, hiding significant internal complexity.
- **Locality**: Every domain rule (format validation, Zip-Slip path sanitization, throughput EMA smoothing, tree hierarchy rollups) lives in exactly one place in `RusZip.Core`.
- **Leverage**: Small core interfaces provide high multiplier value to CLI commands, GUI view models, and future extensions (e.g. web APIs, shell extensions).

---

## 2. Deep Module Specifications

### 2.1 Safe Archive Extraction Pipeline (`SafeArchiveExtractor`)
- **Location**: `RusZip.Core/Engines/SafeArchiveExtractor.cs`
- **Purpose**: Consolidates 4 scattered extraction loops into a single stream-consumer pipeline.
- **Interfaces & Types**:
  - `ExtractionEntry(string RelativePath, bool IsDirectory, long UncompressedSize, DateTimeOffset? ModificationTime, UnixFileMode? UnixMode, Func<CancellationToken, ValueTask<Stream>> OpenStreamAsync)`
  - `IArchiveExtractionSource { IAsyncEnumerable<ExtractionEntry> ReadEntriesAsync(CancellationToken ct); }`
  - `SafeArchiveExtractor.ExtractAllAsync(IArchiveExtractionSource source, string destinationDirectory, bool overwrite, long totalBytes, IProgress<ProgressReport>? progress, CancellationToken ct = default, ExtractionLimits? limits = null, bool totalIsEstimate = false)` returning `Task<ExtractionResult>` (actual streamed bytes/files/entries, never header metadata)
- **Invariants**:
  - **Path Traversal (Zip-Slip)**: Verifies destination root prefix before opening any file stream; throws `SecurityException` upon malicious path detection.
  - **Buffer Pooling**: Rents 80 KB (`ArrayPool<byte>.Shared`) buffers for zero-allocation stream copying.
  - **Metadata Restoration**: Two-pass model restores file timestamps immediately and directory timestamps/POSIX modes in bottom-up (reverse depth) order.
  - **Extraction Guardrails**: Caps cumulative uncompressed output (default 64 GB) and entry count (default 1,000,000), measured from actual streamed bytes and processed entries; exceeding either aborts with `ExtractionLimitExceededException` and cleans up partial output. A `0`/`null` limit means unlimited (see ADR-0007).

### 2.2 Format Capability Model (`ArchiveFormatRegistry`)
- **Location**: `RusZip.Core/Models/ArchiveFormatRegistry.cs`
- **Purpose**: Single source of truth for format detection, bidirectional capabilities, extension aliases, and compression profile limits.
- **Interfaces & Types**:
  - `ArchiveFormatDescriptor(ArchiveFormat Format, string DisplayName, string PrimaryExtension, IReadOnlyList<string> Extensions, bool CanCompress, bool CanDecompress, int MinCompressionLevel, int MaxCompressionLevel, int DefaultCompressionLevel, string MimeType, string CategoryDescription)`
  - `ArchiveFormatRegistry.Detect(string pathOrExtension)`
  - `ArchiveFormatRegistry.TryDetect(string? path, out ArchiveFormatDescriptor? descriptor)`
  - `ArchiveFormatRegistry.IsSupportedArchive(string? path)`
  - `ArchiveFormatRegistry.CompressibleFormats`, `DecompressibleFormats`, `SupportedExtensions`
- **Leverage**:
  - Replaces hardcoded extension arrays and format switch statements in `MainWindowViewModel`, `CompressionSettingsViewModel`, and `CompressCommand`.

### 2.3 Data Metrics & Throughput Tracking (`DataMetricsFormatter` & `ThroughputTracker`)
- **Location**: `RusZip.Core/Models/DataMetricsFormatter.cs`
- **Purpose**: Centralizes byte formatting, compression ratios, and stateful speed smoothing/ETA calculation.
- **Interfaces & Types**:
  - `DataMetricsFormatter`: Pure static functions (`FormatBytes`, `FormatThroughput`, `FormatEta`, `FormatRatio`, `FormatProgress`).
  - `ThroughputTracker`: Stateful tracker managing `Stopwatch`, alpha-smoothed EMA speed calculation, dynamic ETA estimation, and progress strings.
- **Leverage**:
  - Deletes 3 duplicate `FormatBytes` implementations.
  - Strips stopwatch and math logic out of `OperationProgressViewModel` and `CliProgressBridge`.

### 2.4 Headless Archive Tree Projection (`ArchiveHierarchy`)
- **Location**: `RusZip.Core/Models/ArchiveHierarchy.cs`
- **Purpose**: Converts flat archive entry lists into recursive hierarchical trees with automatic size rollups without UI dependencies.
- **Interfaces & Types**:
  - `ArchiveTreeNode(string Name, string RelativePath, bool IsDirectory, long UncompressedSize, long? CompressedSize, DateTimeOffset? LastModified, string Attributes, List<ArchiveTreeNode> Children)`
  - `ArchiveHierarchy.BuildTree(IEnumerable<ArchiveEntry> entries, string? filterText = null)`
- **Leverage**:
  - Removes 70 lines of path tokenization from `ArchiveBrowserViewModel`.
  - Enables headless testing of tree operations. A CLI tree view (`rus-zip list --tree`) is
    future work (per PRD #31) and no such flag ships today.
  - **Desktop grid control**: `ArchiveBrowserViewModel` presents the projected tree through
    ProDataGrid 11.3.x (`Avalonia.Controls.DataGrid` with hierarchical rows) in the Desktop
    browser. ProDataGrid is the MIT-licensed continuation of `Avalonia.Controls.TreeDataGrid`
    by its original author; the TreeDataGrid 11.2+ line moved behind a commercial Avalonia Pro
    license (`AvaloniaUI.Licensing` build gate), so the browser adopted ProDataGrid (issue #51).
    The DataGrid columns live in `ArchiveBrowserView.axaml`; per-column sort comparers are
    exposed by the ViewModel and assigned to the columns from the view code-behind.

### 2.5 CLI Command Execution Pipeline (`CliCommandRunner`)
- **Location**: `RusZip.Cli/Infrastructure/CliCommandRunner.cs`
- **Purpose**: Collapses command lifecycle management, timing, progress bridge dispatch, and exception-to-exit-code translation.
- **Interfaces & Types**:
  - `CliCommandRunner.RunAsync<TResult>(string operationTitle, bool isJson, Func<IProgress<ProgressReport>?, CancellationToken, Task<TResult>> operation, Action<TResult, long>? renderConsoleSummary = null, CancellationToken ct = default, TextWriter? outputWriter = null, bool verboseErrors = false)`
  - `CliCommandRunner.HandleException(Exception ex, bool isJson, TextWriter? writer = null, bool verboseErrors = false)`
  - `CliCommandRunner.EmitError(string code, string message, bool isJson, int exitCode, string? stackTrace = null, TextWriter? writer = null, bool verboseErrors = false)`
- **Leverage**:
  - Standardizes exit codes: `SOURCE_NOT_FOUND` (2), `ARGUMENT_ERROR` (2), `UNSUPPORTED_FORMAT` (2), `SECURITY_VIOLATION` (1), `EXECUTION_ERROR` (1).
  - `ArchiveIntegrityException` (integrity verification) and `ExtractionLimitExceededException` (extraction guardrails) both map to `EXECUTION_ERROR` (1).
  - Turns `CompressCommand`, `ExtractCommand`, and `ListCommand` into declarative parameter mappers.

---

## 3. Prototype Branch Assets

All 5 modules have been verified with complete unit test suites on the following throwaway prototype branches:
- `prototype/safe-archive-extractor` (`6be00e2`)
- `prototype/archive-format-registry` (`155678f`)
- `prototype/data-metrics-formatter` (`84e685f`)
- `prototype/archive-hierarchy` (`d29f6f0`)
- `prototype/cli-command-runner` (`6f7ae57`)

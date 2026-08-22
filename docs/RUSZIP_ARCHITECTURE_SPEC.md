# rus-zip: Architecture Specification & System Blueprint

## 1. System Overview
`rus-zip` is a modern, cross-platform archive management suite written in C# targeting **.NET 10**. It provides:
1. **Core Archive Engine (`RusZip.Core`)**: A headless, high-performance streaming archive library.
2. **AI-Friendly CLI (`RusZip.Cli`)**: A command-line utility with rich ANSI help, copy-pasteable usage examples, automatic zero-argument help display, and `--json` machine-readable output.
3. **Desktop Application (`RusZip.Desktop`)**: A cross-platform GUI built with **Avalonia 11 UI**, featuring virtualized archive inspection (`TreeDataGrid`), real-time progress/cancellation modal overlay, compression level slider with dynamic profile badges, and drag-and-drop file routing.

---

## 2. Supported Format Matrix

| Format | Extension | Container / Compression | Operations Supported | Engine |
| :--- | :--- | :--- | :--- | :--- |
| **.zrus** | `.zrus` | POSIX/PAX Tar + Zstandard (`zstd`) | **Compress & Decompress** | `ZstdTarArchiveEngine` |
| **Zip** | `.zip` | Standard Zip / Zip64 | **Compress & Decompress** | `SharpCompressArchiveEngine` |
| **RAR** | `.rar` | RAR4 & RAR5 | **Decompress Only** | `SharpCompressArchiveEngine` |
| **7-Zip** | `.7z` | 7z Container (LZMA/LZMA2) | **Decompress Only** | `SharpCompressArchiveEngine` |
| **GZip** | `.gz` | GZip compressed stream | **Decompress Only** | `SharpCompressArchiveEngine` |
| **Tar.Gz** | `.tar.gz`, `.tgz` | GZip stream + Tar container | **Decompress Only** | `SharpCompressArchiveEngine` |

---

## 3. Compression Levels & Named Profiles (.zrus)

Zstandard compression spans integer levels **1 through 22**:

| Profile | Level | Use Case & Performance Characteristics |
| :--- | :--- | :--- |
| **Fast** | `3` | High throughput, minimal CPU usage. Optimal for fast local backups. |
| **Balanced** (Default) | `9` | Recommended default balance between compression ratio and speed. |
| **High** | `15` | Higher compression ratio for network distribution and archiving. |
| **Ultra** | `22` | Maximum compression ratio. High CPU and memory utilization. |

---

## 4. Solution Architecture

```
rus-zip/
├── RusZip.slnx
├── CONTEXT.md
├── docs/
│   ├── adr/
│   │   ├── 0001-zrus-format-specification.md
│   │   ├── 0002-four-project-solution-architecture.md
│   │   └── 0003-unified-archive-engine-abstraction.md
│   └── RUSZIP_ARCHITECTURE_SPEC.md
├── src/
│   ├── RusZip.Core/                 # Headless Archive Abstraction & Compression Pipelines
│   │   ├── Abstractions/
│   │   │   └── IArchiveEngine.cs
│   │   ├── Engines/
│   │   │   ├── ZstdTarArchiveEngine.cs
│   │   │   ├── SharpCompressArchiveEngine.cs
│   │   │   └── UnifiedArchiveEngine.cs
│   │   └── Models/
│   │       ├── ArchiveEntry.cs
│   │       ├── ArchiveFormat.cs
│   │       ├── ArchiveRequests.cs
│   │       └── ProgressReport.cs
│   ├── RusZip.Cli/                  # Spectre.Console CLI Executable
│   │   ├── Commands/
│   │   │   ├── CompressCommand.cs
│   │   │   ├── ExtractCommand.cs
│   │   │   └── ListCommand.cs
│   │   ├── Infrastructure/
│   │   │   ├── AiHelpProvider.cs
│   │   │   ├── CliProgressBridge.cs
│   │   │   └── TypeRegistrar.cs
│   │   ├── Models/
│   │   │   └── CliResultModels.cs
│   │   └── Program.cs
│   └── RusZip.Desktop/              # Avalonia 11 Desktop Application
│       ├── ViewModels/
│       │   ├── ArchiveBrowserViewModel.cs
│       │   ├── ArchiveItemViewModel.cs
│       │   ├── CompressionSettingsViewModel.cs
│       │   ├── MainWindowViewModel.cs
│       │   └── OperationProgressViewModel.cs
│       ├── Views/
│       │   ├── ArchiveBrowserView.axaml (.cs)
│       │   ├── CompressionSettingsView.axaml (.cs)
│       │   ├── MainWindow.axaml (.cs)
│       │   └── ProgressOverlay.axaml (.cs)
│       ├── App.axaml (.cs)
│       └── Program.cs
└── tests/
    └── RusZip.Core.Tests/           # xUnit Test Suite
        ├── SharpCompressArchiveEngineTests.cs
        ├── UnifiedArchiveEngineTests.cs
        └── ZstdTarArchiveEngineTests.cs
```

---

## 5. CLI AI-Agent Specification

### Commands
- **Compress**: `rus-zip compress <SOURCE> [DESTINATION] [-l <1-22>] [-p <fast|balanced|high|ultra>] [--json]`
- **Extract**: `rus-zip extract <ARCHIVE> [-o <DESTINATION>] [--overwrite] [--json]`
- **List**: `rus-zip list <ARCHIVE> [--json]`
- **Help**: `rus-zip` or `rus-zip --help`

### Exit Codes
- `0`: Success
- `1`: Engine / Extraction / Compression error
- `2`: Invalid arguments / Source path not found

### JSON Output Schemas
When run with `--json`, `stdout` emits valid JSON:

#### Success Response
```json
{
  "success": true,
  "sourcePath": "/path/to/source",
  "archivePath": "/path/to/output.zrus",
  "format": "zrus",
  "totalFiles": 12,
  "uncompressedBytes": 1048576,
  "compressedBytes": 349525,
  "compressionRatio": 0.3333,
  "elapsedMilliseconds": 128
}
```

#### Error Response
```json
{
  "success": false,
  "error": {
    "code": "SOURCE_NOT_FOUND",
    "message": "Source path '/path/to/nonexistent' does not exist."
  }
}
```

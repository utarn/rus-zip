# rus-zip

A cross-platform desktop application and command-line tool for compressing and decompressing archive files across Windows, Linux, and macOS.

## What is rus-zip?

rus-zip is a single solution built around a headless **Core Engine** (`RusZip.Core`) that provides unified archive abstraction, stream compression, decompression, entry inspection, and progress reporting without UI or CLI dependencies. On top of that core sit two independently buildable front ends:

- **`rus-zip` CLI** (`RusZip.Cli`) — a standalone executable optimized for human terminals and AI agents, with `--json` machine-readable output.
- **rus-zip Desktop** (`RusZip.Desktop`) — an Avalonia UI application with an archive browser, a compression wizard, and a theme picker.

### Formats

**Supported Formats** handled by the engine:

| Direction | Formats |
| --- | --- |
| Compress & Decompress | `.zrus` (Tar+Zstd), `.tar.zstd`, `.tzstd`, `.zst` (Single-file Zstandard stream), `.zip` |
| Decompress Only | `.rar`, `.7z`, `.gz`, `.tar.gz` |

### `.zrus`, `.tar.zstd`, and `.tzstd`

`.zrus` is the default archive format of rus-zip. It combines a Tar container structure (preserving multi-file directory trees, POSIX permissions, and timestamps) with Zstandard (`zstd`) compression, supporting configurable compression levels 1–22. rus-zip also supports standard Tar+Zstandard extensions (`.tar.zstd` and `.tzstd`) as first-class aliases with identical capabilities.

### `.zst` (Single-File Zstandard Stream)

`.zst` provides direct, headerless single-file Zstandard stream compression and decompression without a Tar container wrapper, supporting compression levels 1–22.

### Compression Profiles

**Compression Profiles** map friendly names to Zstandard compression levels:

| Profile | Level | Description |
| --- | --- | --- |
| `fast` | 3 | High speed compression, lower CPU |
| `balanced` | 9 | Default general-purpose profile |
| `high` | 15 | Improved ratio for distribution |
| `ultra` | 22 | Maximum Zstandard compression ratio |

## Platform Support

rus-zip runs on **Windows**, **Linux**, and **macOS**. The solution targets **.NET 10**; all compression/decompression is handled by managed libraries (`ZstdSharp.Port` and `SharpCompress`), so no external native tools or CLI binaries need to be pre-installed on the host.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

Check your installation with:

```bash
dotnet --list-sdks
```

### Running from source

The repository ships platform runner scripts at the root:

| Platform | Command |
| --- | --- |
| macOS / Linux | `./run.sh` |
| Windows | `.\run.ps1` (or `run.bat`) |

```bash
./run.sh --help        # Show available runner commands
./run.sh cli --help    # Run the rus-zip CLI
./run.sh desktop       # Launch the Avalonia Desktop app
./run.sh test          # Run all unit and integration tests
./run.sh build         # Build the entire solution
```

Alternatively, invoke `dotnet` directly:

```bash
dotnet run --project src/RusZip.Cli       # CLI (pass commands after `--`)
dotnet run --project src/RusZip.Desktop   # Desktop app
dotnet test RusZip.slnx                   # Run tests
dotnet build RusZip.slnx                  # Build the solution
```

## CLI Quick Start

The CLI exposes four verbs — `compress`, `append`, `extract`, and `list` (aliases `c`, `a`/`add`, `x`, `l`). After installing the binary (see [Publishing & Installing](#publishing--installing)) you can call `rus-zip` directly; from a source checkout, prefix any example with `./run.sh cli` or `dotnet run --project src/RusZip.Cli --`.

```bash
# Compress a directory into a .zrus archive using the "high" profile
rus-zip compress ./docs backup.zrus --profile high

# Compress multiple source files and directories into an archive
rus-zip compress file1.txt ./data -o backup.zrus

# Compress a single file to a .zst stream
rus-zip compress report.json -o report.json.zst

# Compress a single file into a .zip archive at a specific level
rus-zip compress readme.txt archive.zip -l 9

# Append files or directories to an existing archive (aliases: a, add)
rus-zip append backup.zrus extra.txt ./more-docs

# Append using compress with --append (-a) and update-only (-u) to only overwrite older entries
rus-zip compress extra.txt -o backup.zrus --append --update-only

# List archive contents (human-readable table)
rus-zip list backup.zrus

# List archive contents as machine-readable JSON
rus-zip list backup.zrus --json

# Extract an archive into a directory
rus-zip extract backup.zrus -o ./restored

# Extract with file conflict policy: overwrite (default), skip, or abort
rus-zip extract backup.zrus -o ./restored --conflict skip
```

### Global options

Every command accepts `--json` (`-j`) for machine-readable JSON output and `--verbose-errors` to include full exception stack traces in JSON error output (off by default; enable only when diagnosing failures).

### CLI Command Options

#### `compress` (alias: `c`)

| Option | Flag | Default | Description |
| --- | --- | --- | --- |
| `<SOURCES...>` | Argument | (Required) | Files or directories to compress |
| `-o`, `--output <PATH>` | Option | `<SOURCE>.zrus` | Destination archive path |
| `-l`, `--level <LEVEL>` | Option | `9` (Zstd) / `9` (Zip) | Compression level (`0-9` for `.zip` where `0` = Store, `1-22` for `.zrus`) |
| `-p`, `--profile <PROFILE>` | Option | `balanced` | Compression profile for `.zrus`: `fast` (3), `balanced` (9), `high` (15), `ultra` (22) |
| `-a`, `--append` | Option | `off` | Append sources to an existing archive instead of overwriting |
| `-u`, `--update-only` | Option | `off` | When appending, only replace existing entries if the source file is strictly newer |

#### `append` (aliases: `a`, `add`)

| Option | Flag | Default | Description |
| --- | --- | --- | --- |
| `<ARCHIVE>` | Argument | (Required) | Path to the target archive file (`.zrus`, `.zip`) |
| `<SOURCES...>` | Argument | (Required) | Files or directories to append |
| `-l`, `--level <LEVEL>` | Option | `9` | Compression level (`0-9` for `.zip`, `1-22` for `.zrus`) |
| `-p`, `--profile <PROFILE>` | Option | `balanced` | Compression profile for `.zrus` |
| `-u`, `--update-only` | Option | `off` | Only replace existing entries if the source file is strictly newer |

#### `extract` (alias: `x`)

| Option | Flag | Default | Description |
| --- | --- | --- | --- |
| `<ARCHIVE>` | Argument | (Required) | Path to the archive file |
| `-o`, `--output <DESTINATION>` | Option | Current directory | Directory to extract contents into |
| `-c`, `--conflict <POLICY>` | Option | `overwrite` | Conflict resolution policy: `overwrite`, `skip`, `abort` |
| `--no-overwrite` | Option | `off` | Do not overwrite existing files; abort (exit 1) on conflict (equivalent to `--conflict abort`) |
| `--max-uncompressed-size <SIZE>` | Option | `64GB` | Max cumulative uncompressed output; `0` = unlimited |
| `--max-entries <COUNT>` | Option | `1,000,000` | Max entries processed; `0` = unlimited |

#### `list` (alias: `l`)

| Option | Flag | Default | Description |
| --- | --- | --- | --- |
| `<ARCHIVE>` | Argument | (Required) | Path to the archive file |

### Extraction guardrails

Every archive is treated as untrusted: extraction aborts hard when a guardrail is exceeded. Limits are measured from actual streamed bytes and processed entries — never spoofable header metadata.

```bash
rus-zip extract archive.zip -o ./out --max-uncompressed-size 10GB
rus-zip extract archive.zip -o ./out --max-entries 100000
rus-zip extract archive.zip -o ./out --conflict abort
```

| Flag | Default | Meaning |
| --- | --- | --- |
| `--max-uncompressed-size <bytes\|human>` | `64GB` | Max cumulative uncompressed output; `0` = unlimited |
| `--max-entries <n>` | `1,000,000` | Max entries processed; `0` = unlimited |
| `-c`, `--conflict <overwrite\|skip\|abort>` | `overwrite` | Conflict handling policy for existing destination files |
| `--no-overwrite` | off | Never overwrite existing files; abort (exit 1) naming conflicting path |

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success |
| `1` | Execution / engine error, security violation, or guardrail exceeded |
| `2` | Invalid arguments, path not found, or unsupported format |

## Desktop App

The rus-zip Desktop application is built with Avalonia UI and includes:

- **Archive browser** — browse archive contents in a hierarchical ProDataGrid with size rollups, breadcrumbs, and context menus; extract individual items or the full archive.
- **Compression wizard** — pick a source, destination format (`.zrus` or `.zip`), compression profile, and level.
- **Themes** — switch between `System`, `Dark`, and `Light` themes from the main window.

## Installation & Package Managers

### macOS & Linux via Homebrew (Custom Tap)

You can install `rus-zip` CLI or Desktop directly from this repository using Homebrew:

```bash
# Add this repository as a tap
brew tap utarn/rus-zip

# Install the CLI tool
brew install rus-zip

# Install the Desktop GUI application (macOS)
brew install --cask rus-zip
```

Or install directly in a single command without explicit tapping:

```bash
# CLI
brew install utarn/rus-zip/rus-zip

# Desktop GUI (.app)
brew install --cask utarn/rus-zip/rus-zip
```

---

### Windows via WinGet

Install the portable Windows bundle (containing both `rus-zip` CLI and `RusZip` Desktop):

```powershell
winget install rus.zip
# OR
winget install rus-zip
# OR
winget install ruszip
```

---

### Linux via Flatpak & Snap

```bash
# Flatpak (Desktop GUI)
flatpak install flathub com.ruszip.desktop

# Snap (CLI & Desktop)
snap install rus-zip
```

---

### Standalone Executable Downloads

Pre-built self-contained binaries for all platforms are available on [GitHub Releases](https://github.com/utarn/rus-zip/releases):
- **Windows (`win-x64`)**: `rus-zip-win-x64.zip` (Portable CLI + Desktop `.exe`)
- **macOS Apple Silicon (`osx-arm64`)**: `rus-zip-cli-osx-arm64.tar.gz` (CLI) and `RusZip-mac-arm64.zip` (Desktop `.app`)
- **macOS Intel (`osx-x64`)**: `rus-zip-cli-osx-x64.tar.gz` (CLI) and `RusZip-mac-x64.zip` (Desktop `.app`)
- **Linux (`linux-x64`)**: `rus-zip-linux-x64.tar.gz` (Portable CLI + Desktop)
- **Integrity**: Every release publishes a cryptographically signed `SHA256SUMS.txt`.

---

## Publishing & Local Installation

The `scripts/` directory contains cross-platform publish and install scripts.

### Publish self-contained binaries

```bash
./scripts/publish.sh --rid linux-x64      # macOS/Linux; default RID osx-arm64
.\scripts\publish.ps1 -Rid win-x64        # Windows; default RID win-x64
```

Output goes to `dist/<rid>/` with the CLI binary (`rus-zip` / `rus-zip.exe`) and the Desktop app bundle/executable.

### Install the CLI onto your PATH

```bash
./scripts/install.sh                      # macOS/Linux → $HOME/.local/bin (or /usr/local/bin as root)
.\scripts\install.ps1                     # Windows → %LOCALAPPDATA%\Programs\rus-zip (adds to User PATH)
```

Re-running the same version is a no-op; a new version keeps a single backup (`rus-zip.bak`). Use `--uninstall` / `-Uninstall` to remove it.

### Version Management & Bumping

To bump semantic versions across the solution:

```bash
./bump_version.sh patch    # 1.0.0 -> 1.0.1 (default)
./bump_version.sh minor    # 1.0.0 -> 1.1.0
./bump_version.sh major    # 1.0.0 -> 2.0.0
./bump_version.sh 1.2.3    # Explicit version
```

On PowerShell:
```powershell
.\bump_version.ps1 patch
.\bump_version.ps1 minor
.\bump_version.ps1 major
.\bump_version.ps1 1.2.3
```

## Architecture & Documentation

- **[`CONTEXT.md`](CONTEXT.md)** — the project glossary and ubiquitous language (rus-zip, `.zrus`, Core Engine, Archive Format Registry, Safe Archive Extractor, Data Metrics Formatter & Throughput Tracker, Archive Hierarchy, CLI Command Runner, Supported Formats, Extraction Guardrails).
- **[`docs/adr/`](docs/adr/)** — Architecture Decision Records for the `.zrus` format, four-project solution, unified engine, safe extraction, format registry, extraction guardrails, file associations, desktop menu navigation, compact toolbar, macOS chrome alignment, and display naming.
- **[`docs/RUSZIP_ARCHITECTURE_SPEC.md`](docs/RUSZIP_ARCHITECTURE_SPEC.md)** — the deepened architecture specification for the core modules.

## Building from Source

```bash
dotnet build RusZip.slnx -c Release
```

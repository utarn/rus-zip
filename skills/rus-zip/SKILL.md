---
name: rus-zip
description: >
  Compress, decompress, extract, list, and integrity-test archives with the
  rus-zip CLI. Use when the user asks to compress or zip files or folders,
  create an archive or backup, extract or unpack an archive, look inside an
  archive without extracting, verify an archive is not corrupted, or convert
  between archive formats — including .zrus (Tar+Zstandard), .zip, .tar.zst,
  and decompress-only formats like .rar, .7z, .gz, and .tar.gz. Also use when
  a script needs machine-readable (--json) archive output.
license: MIT
metadata:
  author: utarn
  version: "1.0.0"
---

# rus-zip

Cross-platform archive CLI (Windows/macOS/Linux). Native format is **`.zrus`**
(Tar container + Zstandard). Every command supports `--json` for
machine-readable output.

## Preflight

Check the CLI is installed first:

```bash
rus-zip --version
```

If the command is not found, install it (no other tools such as `zstd` or
`unrar` are needed — everything runs in-process):

| OS | Install |
| --- | --- |
| Windows | `winget install rus.zip`, or download `rus-zip-<version>-setup.exe` from <https://github.com/utarn/rus-zip/releases> |
| macOS | `brew tap utarn/rus-zip && brew trust utarn/rus-zip && brew install rus-zip` |
| Linux | Download `rus-zip-cli-linux-x64` from <https://github.com/utarn/rus-zip/releases>, `chmod +x`, move onto `PATH` |

After installing, re-run `rus-zip --version` to confirm. If it still fails,
stop and tell the user rather than improvising.

Licensing: `extract`, `list`, and `test` are free. `compress`, `append`, and
`delete` require a license token in the `RUSZIP_LICENSE` environment variable.
If compress fails with a license error, tell the user — never ask them to paste
secrets into the transcript.

## Choosing a format

| Goal | Format |
| --- | --- |
| Default for backups, folders, anything rus-zip-only | `.zrus` — Tar + Zstandard, levels 1–22, encryption, split volumes, incremental baselines |
| Interchange with other tools (Explorer, `unzip`, …) | `.zip` — levels 0–9 (0 = Store) |
| Stream-compress a single file for Zstandard consumers | `.tar.zst` (or `.tzst`) |
| Decompress-only inputs | `.rar`, `.7z`, `.gz`, `.tar.gz` (also `.tar`, `.zst`) |

When the user just says "compress this" or "back this up", use `.zrus` with the
default profile.

## Commands

Global flags on every command: `-j, --json` (machine-readable output — use it
whenever another tool parses the output), `--verbose-errors` (stack traces in
JSON errors, only when diagnosing).

### compress

```bash
rus-zip compress <SOURCES...> [OPTIONS]
```

Destination: `-o <PATH>`; with a single source it defaults to `<SOURCE>.zrus`.
With multiple sources, the last argument is the destination if it ends in
`.zrus`/`.zip`; otherwise `-o` is required.

| Flag | Use when |
| --- | --- |
| `-o, --output <PATH>` | Setting the archive path explicitly |
| `--profile <fast\|balanced\|high\|ultra>` | Picking speed vs. ratio by name for `.zrus` (levels 3/9/15/22) |
| `-l, --level <LEVEL>` | Fine-tuning: 1–22 for `.zrus`, 0–9 for `.zip` (default 9) |
| `-p, --password <PWD>` | Encrypting the archive |
| `-s, --split <SIZE>` | Splitting into volumes, e.g. `100MB`, `1GB` (min 64KB; files named `.part1.zrus`, …) |
| `-a, --append` | Adding sources to an existing archive instead of replacing it |
| `-u, --update-only` | With append: replace entries only when the source file is strictly newer |
| `-b, --baseline <PATHS>` | Incremental (differential) archive against baseline `.zrus` file(s) |
| `--allow-empty` | With baseline: allow an archive with zero changes |

### extract

```bash
rus-zip extract <ARCHIVE> [OPTIONS]
```

Extracts into the current directory unless `-o` is given.

| Flag | Use when |
| --- | --- |
| `-o, --output <DESTINATION>` | Extracting somewhere other than the current directory |
| `--no-overwrite` | Existing files at the destination must not be touched — extraction aborts (exit 1) naming the conflicting path |
| `-c, --conflict <overwrite\|skip\|abort>` | Choosing a policy for name collisions |
| `--max-uncompressed-size <SIZE>` | Raising/lowering the decompression-bomb cap (default 64GB; `0` = unlimited) |
| `--max-entries <COUNT>` | Raising/lowering the entry-count cap (default 1,000,000; `0` = unlimited) |
| `-p, --password <PWD>` | Encrypted archive (no interactive prompt in scripts) |
| `-b, --baseline <PATHS>` | Extracting a differential `.zrus` on top of its baseline archive(s) |

### list / test

```bash
rus-zip list <ARCHIVE> [-p <PWD>] [--json]   # inspect contents without extracting
rus-zip test <ARCHIVE> [-p <PWD>] [--json]   # full integrity check, no files written
```

### append

```bash
rus-zip append <ARCHIVE> <SOURCES...> [-u] [-p <PWD>] [--profile P] [-l N]
```

Adds files/directories to an existing `.zrus`; `-u`/`--update-only` skips
sources that are not strictly newer than their archive entries.

## Safety defaults

- **Never overwrite without asking.** `compress` replaces an existing archive
  silently, and `extract` overwrites existing files by default. Before running,
  check whether the target archive or destination directory already exists; if
  it does, confirm with the user first, or pick a fresh name and use
  `--no-overwrite` / `-c skip` for extraction.
- **Verify large archives after creating them.** After compressing a large or
  important tree, run `rus-zip test <ARCHIVE>` and only report success when it
  passes.
- **Use `--json` when a tool consumes the output.** Plain output is a styled
  terminal table; scripts and agents should parse `--json` and check the
  `success` field.
- **Prefer explicit destinations.** Pass `-o` rather than relying on defaults,
  and extract into a dedicated directory, not the working directory.
- **Leave the bomb caps alone** unless the user's archive genuinely exceeds
  them; raising `--max-uncompressed-size`/`--max-entries` disables a
  corruption-and-malware guard.

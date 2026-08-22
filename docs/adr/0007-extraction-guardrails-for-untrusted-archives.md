# 0007: Extraction Guardrails for Untrusted Archives

We decided that extraction enforces hard-fail limits on cumulative uncompressed output size and entry count, rather than warning, because a warning still fills the disk.

## Context

Every archive is treated as untrusted (see `CONTEXT.md`); entry metadata is attacker-controlled. The 2026-08-22 audit (PRD #38, finding F-04) confirmed a 194 KB crafted zip expanded to 200 MB with no limit, and that pre-scan totals derived from entry metadata are spoofable (an 8-byte entry was reported as 1.9 GB) — metadata can drive progress bars but must never drive enforcement.

## Decision

1. `SafeArchiveExtractor.ExtractAllAsync` (`src/RusZip.Core/Engines/SafeArchiveExtractor.cs`) enforces two caps measured from **actual streamed bytes and processed entries**, never header metadata: cumulative uncompressed output (default 64 GB; 0 = unlimited) and entry count (default 1,000,000; 0 = unlimited).
2. Exceeding a cap aborts extraction with a dedicated exception mapped to exit code 1 (`EXECUTION_ERROR`), cleans up partial output, and names both the limit hit and the override flag.
3. Limits are user-configurable: additive CLI flags (`--max-uncompressed-size`, `--max-entries`) and a Desktop settings surface. Overriding is an explicit opt-in, not a prompt — the CLI stays non-interactive.
4. The Desktop browser refuses tree construction beyond the entry-count cap with a clear message instead of exhausting memory.

```csharp
public sealed record ExtractionLimits(
    long? MaxCumulativeUncompressedBytes,   // default 64 GB; null = unlimited
    int? MaxEntryCount);                    // default 1_000_000; null = unlimited
```
Extend `ArchiveExtractionRequest` (`src/RusZip.Core/Models/ArchiveRequests.cs`) with `ExtractionLimits? Limits`.

## Implementation Status (issue #42)

- **Enforcement** (`SafeArchiveExtractor.ExtractAllAsync`): both caps are measured from actual
  streamed bytes (`processedBytes`) and processed entries (`processedEntries`), never header metadata.
  A cap hit throws `ExtractionLimitExceededException` (`src/RusZip.Core/Engines/ExtractionLimitExceededException.cs`)
  with a message naming the limit and its CLI override flag. Partial output is cleaned up via the same
  `createdPaths` mechanism used for security aborts. Defaults are exposed as
  `SafeArchiveExtractor.DefaultMaxCumulativeUncompressedBytes` (64 GB) and
  `SafeArchiveExtractor.DefaultMaxEntryCount` (1,000,000); a field of `0` or `null` means unlimited.
- **Real totals** (`ExtractionResult`): `SafeArchiveExtractor` now returns actual bytes/files/entries
  processed, and `IArchiveEngine.ExtractAsync` returns `Task<ExtractionResult>`. The CLI `extract`
  summary therefore reports real streamed totals instead of trusting header-declared sizes — an 8-byte
  stored entry no longer reports as "1.9 GB" after extraction.
- **CLI** (`extract`): additive flags `--max-uncompressed-size <bytes|human>` (e.g. `10GB`, `500MB`;
  `0` = unlimited) and `--max-entries <n>` (`0` = unlimited). Human sizes parse via
  `DataSizeParser` (`src/RusZip.Core/Models/DataSizeParser.cs`). The exception is mapped to exit code 1
  (`EXECUTION_ERROR`) in `CliCommandRunner`.
- **Desktop**: a small extraction-limits row in the archive browser surfaces the same two limits
  (`ExtractionSettingsViewModel`), with sensible defaults matching the Core defaults. The request
  built by `MainWindowViewModel` carries `Limits`.
- **GUI memory guard (F-36)**: `ArchiveBrowserViewModel.LoadEntries` refuses tree construction when the
  entry count exceeds the entry-count cap and shows an empty-state error banner instead of exhausting
  memory. The cap is checked before any `ArchiveItemViewModel` / `HierarchicalModel` / row structure is
  allocated: the entry list is dropped on the abort path, and `RebuildGridSource` no-ops while a load
  error is showing, so a later filter keystroke cannot rebuild a hostile tree after the initial check.
- **F-33 (double decompression pass)**: the pre-scan is kept. Eliminating it would force the progress
  bar to be indeterminate for the whole operation (a running-total can never reach a retroactive
  percentage), which is a UX regression for legitimate archives. The pre-scan total is labeled as an
  estimate (`ProgressReport.IsTotalEstimate`) in progress UI and is never read for enforcement. If a
  single-pass running-total progress design is later wanted, it can be introduced without changing
  enforcement, which already only depends on streamed bytes.

## Considered Options

- **Warn-and-continue** — rejected: silent disk-fill still happens.
- **Interactive prompt** — rejected: breaks CLI non-interactivity and `--json` consumers.

## Consequences

Progress totals shown before streaming begins are estimates (metadata-derived, spoofable) and are
labeled as such; true totals emerge while streaming. Legitimate archives larger than the defaults
require an explicit override — the safe default errs toward refusing a hostile archive over assuming a
benign one. Implementation subissue: #42 (blocked relationship: integrity verification #44 lands after
this, same code path).

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

## Considered Options

- **Warn-and-continue** — rejected: silent disk-fill still happens.
- **Interactive prompt** — rejected: breaks CLI non-interactivity and `--json` consumers.

## Consequences

Progress totals shown before streaming begins are estimates (metadata-derived, spoofable) and may be labeled as such; true totals emerge while streaming. Legitimate archives larger than the defaults require an explicit override — the safe default errs toward refusing a hostile archive over assuming a benign one. Implementation subissue: #42 (blocked relationship: integrity verification #44 lands after this, same code path).

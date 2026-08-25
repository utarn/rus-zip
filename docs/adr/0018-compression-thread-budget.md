# Compression Thread Budget

rus-zip exposes a single user-configurable **Compression Thread Budget** that sizes Zstandard's internal worker pool (`ZSTD_c_nbWorkers`) for all `.zrus`/`.zst` compression pipelines. An unset (Auto) budget resolves to half the **Logical Processors** (rounded down, minimum 1) on every launch; an explicit budget is clamped to `[1, Logical Processors]` at the moment an operation starts. Decompression is deliberately left unthreaded — there is no decompression setting.

Domain terms: see `CONTEXT.md` — *Logical Processors*, *Thread Budget*, *Stream Workers*.

## Considered Options

- **Two settings (compression + decompression).** Rejected: zstd single-frame decompression is single-threaded by design (no `ZSTD_d_nbWorkers` exists), and a `.zrus` archive is one Tar stream through one zstd decoder — entry *N* cannot decode before entries 1…N−1. A decompression setting would be a dead control connected to nothing.
- **SharpSevenZip (7z.dll) for multithreaded `.zip` creation.** Rejected after investigation: the wrapper is Windows-only in practice (defaults to `x86`/`x64` + `7z.dll`, no Linux/macOS COM-export natives shipped, Apple Silicon unaddressed), while rus-zip is cross-platform by charter; its own benchmarks show extraction ~2× slower than SharpCompress on .NET 8; and it would add native bundling plus LGPL-3.0/unRAR notice obligations for one code path. 7-Zip has supported multithreaded ZIP compression since 4.43 beta (2006), so the rejection is about platform cost, not capability. SharpCompress is retained for all `.zip` work and all archive reading.
- **Managed per-entry parallel extraction ("Entry Workers") for `.zip`/`.rar`/`.7z`.** Viable (random-access containers decode entries independently) but cut from scope: the settled scope is `.zrus`-only threading using library-native multithreading only. The request-field and settings plumbing below accommodate adding an extraction budget later without schema changes.
- **Keep the status-quo default (all cores, hardcoded).** Rejected: the halved default is a deliberate, accepted regression — it leaves headroom for the interactive UI during operations and is kinder to thermals/battery. Users who want full-core compression set the budget explicitly.

## Consequences

- Default `.zrus`/`.zst` compression (create, append, delete/recompress) drops from `Environment.ProcessorCount` zstd workers to `max(1, floor(n/2))` — intentional and user-overridable.
- No decompression threading anywhere: extraction, test, preview, and chain extraction remain sequential, as does all `.zip` creation.
- A new persistence home `~/.rus-zip/` is introduced and the MRU file migrates into it (see below).
- The CLI gains no flags in this change; the request-field plumbing makes that a small follow-up.

## Technical Design

### Budget resolution (Core)

Pure, testable helper; engines never read preferences:

```csharp
public static class ThreadBudget
{
    /// <param name="requested">User-specified budget; null = Auto.</param>
    /// <param name="logicalProcessors">OS-reported count (SMT included).</param>
    public static int Resolve(int? requested, int logicalProcessors) =>
        Math.Clamp(requested ?? Math.Max(1, logicalProcessors / 2), 1, logicalProcessors);
}
```

### Request plumbing (Core)

`ArchiveCompressionRequest` (`src/RusZip.Core/Models/ArchiveRequests.cs:5-27`) — alongside the existing level/password/split-size fields:

```csharp
/// <summary>
/// Worker threads for compression pipelines. null = Auto (max(1, floor(LogicalProcessors / 2))).
/// Clamped to [1, LogicalProcessors] when the operation starts.
/// </summary>
public int? CompressionWorkerCount { get; init; }
```

The same field must reach append/delete — they are compression pipelines (full decompress-and-recompress rewrites).

### Engine call sites (Core)

Replace the hardcoded `Environment.ProcessorCount` at the four `ZSTD_c_nbWorkers` sites in `src/RusZip.Core/Engines/ZstdTarArchiveEngine.cs` with `ThreadBudget.Resolve(request.CompressionWorkerCount, Environment.ProcessorCount)`, preserving the existing "only set the parameter when the value is > 1" shape:

| Site | Pipeline |
|------|----------|
| `:188-191` | `.zst` single-file compression |
| `:266-269` | `.zrus` archive compression |
| `:710-713` | `.zrus` append (recompress) |
| `:1045-1048` | `.zrus` delete (recompress) |

### Settings persistence (Desktop)

New `IAppSettingsService` + `AppSettings` cloned from the `JsonRecentArchivesService` pattern (`src/RusZip.Desktop/Services/JsonRecentArchivesService.cs`: atomic tmp-file write at `:149-152`, corrupt-file fallback to defaults at `:65-69`):

```csharp
public sealed record AppSettings(int? CompressionWorkerCount = null);

public interface IAppSettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
```

- Schema: `{ "compressionWorkerCount": null }` — `null` **is** Auto; absent file = Auto. Never freeze a resolved number into storage.
- Path: `$HOME/.rus-zip/app-settings.json` (Windows: `%USERPROFILE%\.rus-zip\`), via `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)`.
- MRU migration: `JsonRecentArchivesService.GetDefaultStoragePath` (`:33-41`) moves to `~/.rus-zip/recent-archives.json`; on load, if the new path is absent and the legacy path exists, copy once, best-effort (swallow IO errors). Invariant: *all rus-zip persistence lives under `~/.rus-zip/`*.

### Settings UI (Desktop)

A "Performance" card added atop the existing card stack in `src/RusZip.Desktop/Views/SettingsView.axaml` (card idiom: `Border CornerRadius="8"` with `CardBackgroundFillColor*`/`SurfaceStrokeColorDefault`, as in the two existing cards at `:9-30` and `:33-96`). Contents: one thread control with Auto semantics (Auto checkbox/segment + numeric override) and a read-only `Logical Processors: {Environment.ProcessorCount}` line showing the ceiling. No new dialogs, toolbar entries, or chrome (ADR 0017 untouched).

`SettingsViewModel` (`src/RusZip.Desktop/ViewModels/SettingsViewModel.cs`) gains an `IAppSettingsService` dependency (currently constructed inline at `MainWindowViewModel.cs:232`; DI registration lives at `App.axaml.cs:41-51`). `CompressionSettingsViewModel.CreateCompressionRequest()` (invoked at `MainWindowViewModel.cs:586`) stamps `CompressionWorkerCount` from loaded settings into every outgoing compression/append/delete request.

### Tests

- `ThreadBudget.Resolve` unit tests (null/odd counts/clamp bounds/hand-edited oversized values).
- `IAppSettingsService` round-trip, corruption fallback, Auto-preserving write.
- MRU migration (legacy present → copied once; legacy absent → fresh).
- Desktop headless tests extend the existing `SettingsViewModelTests` / `SettingsViewTests` suites: card renders, Logical Processors line shows `Environment.ProcessorCount`, Auto ⇄ override toggling.

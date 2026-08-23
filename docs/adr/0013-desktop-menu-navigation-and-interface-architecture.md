# 0013: Desktop Menu, Navigation, and Interface Architecture

We decided to implement a full cross-platform application menu bar, a persistent Recent Archives (MRU) manager with empty-state quick-launch cards, standard desktop keyboard accelerators, a sandboxed entry preview pipeline, a non-materializing archive integrity testing engine, an archive/entry properties inspector, and a 3-segment status bar.

## Context
Previously, the `rus-zip` desktop application featured only a flat horizontal toolbar within the custom window title bar. Key desktop archive utility capabilities were missing or unexposed:
1. No top-level desktop menu hierarchy (`File`, `Edit`, `View`, `Archive`, `Tools`, `Help`) or cross-platform macOS `NativeMenu` integration.
2. No persistent history of recently opened archives (MRU) in either the menu or the empty drag-and-drop landing page.
3. No standard desktop keyboard accelerators (`Ctrl+N`, `Ctrl+O`, `Ctrl+W`, `Ctrl+E`, `Ctrl+Shift+E`, `Ctrl+T`, `Ctrl+A`, `Ctrl+F`, `Alt+Enter`, `F1`, `F5`).
4. No double-click / `Enter` entry previewing with sandboxed temporary extraction and default OS application launching.
5. No non-materializing archive integrity verification ("Test Archive") to validate blocks and headers without disk I/O.
6. No detailed container and item metadata inspection dialog (`Alt+Enter`).
7. The status bar was a single text string lacking real-time selection stats, format read-write badges, and extraction guardrail limit indicators.

## Decision

1. **Cross-Platform Application Menu Bar**:
   - Introduce a structured menu bar on `MainWindow`:
     - **`File`**: New Archive (`Ctrl+N`), Open Archive... (`Ctrl+O`), Recent Archives submenu with Clear Recent option, Close Archive (`Ctrl+W`), Exit (`Alt+F4`).
     - **`Edit`**: Select All (`Ctrl+A`), Invert Selection, Copy Relative Path (`Ctrl+C`), Delete Selected (`Del`).
     - **`View`**: Refresh / Reload (`F5`), Expand All (`Ctrl+Shift+E`), Collapse All, Filter Focus (`Ctrl+F`).
     - **`Archive`**: Add Files / Append... (`Ctrl+Shift+A`), Extract All (`Ctrl+E`), Extract Selected..., Test Archive Integrity (`Ctrl+T`), Archive Properties (`Alt+Enter`).
     - **`Tools`**: Settings (`Ctrl+,`), Theme Switcher, Set File Associations.
     - **`Help`**: Documentation, Supported Formats Matrix, About rus-zip (`F1`).
   - On **macOS**, seamlessly map top-level menus to `NativeMenu` to match Apple Human Interface Guidelines. On **Windows & Linux**, render the themed in-window menu bar below the custom title bar.

2. **Recent Archives History (MRU) Subsystem**:
   - Create `IRecentArchivesService` in `RusZip.Desktop.Services` storing up to 10 recent archive paths in `~/.config/rus-zip/recent-archives.json` (Linux/macOS) or `%APPDATA%\rus-zip\recent-archives.json` (Windows).
   - Render recent archives under `File -> Recent Archives` and as interactive clickable quick-launch cards on the empty landing state when no archive is open.
   - Missing or moved files trigger a clean notification offering to remove the stale path from history.

3. **Archive Preview Session Pipeline**:
   - Create `IArchivePreviewService` in `RusZip.Desktop.Services`.
   - On double-click or `Enter` on an archive file entry in `ArchiveBrowserView`, extract the entry safely to `Path.GetTempPath()/rus-zip-preview/{Guid}/{FileName}` and launch it using `Process.Start` with `UseShellExecute = true`.
   - All preview session temp folders are tracked and automatically purged on archive close and application shutdown.

4. **Archive Integrity Test ("Test Archive") Engine**:
   - Add `Task<ArchiveTestResult> TestArchiveAsync(...)` to `IArchiveEngine` in `RusZip.Core.Abstractions`.
   - Stream through the entire archive using `SafeArchiveExtractor` in verification mode (decompressing all blocks and computing CRC/Tar checksums without creating filesystem files).
   - Show progress via `ProgressOverlay` and display a comprehensive test result modal summarizing total entries verified, uncompressed bytes scanned, throughput (MB/s), elapsed time, and error diagnostics if any corrupt blocks exist.

5. **Archive & Entry Properties Inspector**:
   - Create `ArchivePropertiesDialog.axaml` and `ArchivePropertiesViewModel.cs` triggered via `Alt+Enter`, menu, or context menu.
   - Display archive metadata (format, compression level, dictionary size, total uncompressed/compressed sizes, ratio, entry count) or single/multi-entry metadata (POSIX octal mode, timestamps, attributes, compressed/uncompressed sizes).

6. **Segmented Status Bar**:
   - Replace the single text line in `MainWindow.axaml` with a 3-segment status bar:
     - **Left Segment**: Operational state (`Ready`, `Filtering...`, `Loaded 540 entries`).
     - **Middle Segment**: Live selection metrics (`3 items selected (24.1 MB uncompressed)`).
     - **Right Segment**: Container format badge (`[ .zrus (Tar+Zstd) | Read-Write ]` or `[ .rar | Read-Only ]`) and Guardrail limit badge (`[ Limit: 64 GB ]`).

7. **Smart Category Filter in Archive Browser**:
   - Enhance the filter bar in `ArchiveBrowserView` with wildcard globbing (`*.log`, `!*.tmp`) and quick category chips (`All`, `Documents`, `Images`, `Code`, `Media`, `Archives`).

## Technical Architecture & Code Contracts

### 1. `IRecentArchivesService` in `RusZip.Desktop.Services`
```csharp
namespace RusZip.Desktop.Services;

public interface IRecentArchivesService
{
    IReadOnlyList<string> RecentPaths { get; }
    event EventHandler? RecentPathsChanged;
    Task LoadAsync();
    Task AddRecentPathAsync(string path);
    Task RemoveRecentPathAsync(string path);
    Task ClearRecentPathsAsync();
}
```

### 2. `IArchivePreviewService` in `RusZip.Desktop.Services`
```csharp
namespace RusZip.Desktop.Services;

public interface IArchivePreviewService : IAsyncDisposable
{
    Task<bool> PreviewEntryAsync(string archivePath, ArchiveItemViewModel item, CancellationToken cancellationToken = default);
    Task CleanupSessionAsync();
}
```

### 3. `ArchiveTestResult` and `IArchiveEngine.TestArchiveAsync` in `RusZip.Core`
```csharp
namespace RusZip.Core.Models;

public sealed record ArchiveTestResult(
    bool Success,
    string ArchivePath,
    int TotalEntries,
    int CorruptedEntriesCount,
    long UncompressedBytes,
    long ElapsedMilliseconds,
    IReadOnlyList<string> Errors
);

namespace RusZip.Core.Abstractions;

public interface IArchiveEngine
{
    // ... existing methods ...
    Task<ArchiveTestResult> TestArchiveAsync(
        string archivePath,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
```

### 4. `ArchivePropertiesViewModel` in `RusZip.Desktop.ViewModels`
```csharp
namespace RusZip.Desktop.ViewModels;

public partial class ArchivePropertiesViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "Properties";
    [ObservableProperty] private string _archivePath = string.Empty;
    [ObservableProperty] private string _formatName = string.Empty;
    [ObservableProperty] private string _formattedUncompressedSize = string.Empty;
    [ObservableProperty] private string _formattedCompressedSize = string.Empty;
    [ObservableProperty] private string _formattedRatio = string.Empty;
    [ObservableProperty] private int _totalFiles;
    [ObservableProperty] private int _totalDirectories;
    [ObservableProperty] private string _permissions = string.Empty;
    [ObservableProperty] private string _modifiedDate = string.Empty;
}
```

## Consequences
- **Positive**: Complete UI/UX parity with industry-standard archive tools; intuitive desktop menu bar with cross-platform native macOS support; zero-disk-pollution file previewing; instantaneous archive integrity validation; rich status bar telemetry.
- **Negative / Tradeoffs**: Managing temporary preview file lifetimes requires cleanup hooks in window lifetime events; testing massive multi-gigabyte archives requires cancellation and background task coordination.

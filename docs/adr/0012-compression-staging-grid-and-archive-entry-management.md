# 0012: Compression Staging Grid and Archive Entry Management

We decided to provide a hierarchical Compression Staging Grid for multi-source archive creation with granular exclusion management, and implement an atomic archive deletion pipeline along with desktop workflows for appending and deleting entries in mutable archives (`.zrus`, `.zip`).

## Context
Previously, the `rus-zip` Desktop GUI only permitted browsing a single source file or folder (or receiving dropped files) into a basic text box when creating archives. There was no interactive data grid for staging multiple disparate files and directories, inspecting their filesystem metadata (size, timestamps, attributes, file types), removing staged items, or excluding unwanted nested files prior to compression. Furthermore, when viewing an open archive in the Archive Browser, users could not append new files or delete entries from mutable archives (`.zrus`, `.zip`).

## Decision

1. **Compression Staging Grid & Wizard Overhaul**:
   - Upgrade `CompressionSettingsViewModel` and `CompressionSettingsView.axaml` with an interactive ProDataGrid (`HierarchicalModel` with `DataGridHierarchicalColumn`) displaying staged files and folders with full filesystem metadata:
     - **Name** with file/directory type vector icons.
     - **Size** (formatted uncompressed bytes).
     - **Modified** (filesystem last write timestamp).
     - **Attributes** (POSIX/Windows permissions).
     - **Source Path** (full filesystem path).
   - Provide explicit toolbar actions:
     - `Add File(s)...`: Opens multi-selection file picker (`AllowMultiple = true`) to append files.
     - `Add Folder(s)...`: Opens folder picker to append directory trees.
     - `Remove Selected`: Un-stages root items or marks nested child items as excluded.
     - `Clear All`: Clears all staged items.
     - Keyboard shortcut: `Delete` / `Backspace` removes/excludes selected rows.
   - Support fine-grained exclusions: Child items within staged folders can be excluded from compression (rendered with muted opacity, strikethrough styling, and an include/exclude toggle).
   - Smart Auto-Naming: Destination archive path auto-generates based on the primary item name or parent folder name, with an override lock when manually edited.

2. **Core Engine Multi-Source & Exclusion Contract**:
   - Extend `ArchiveCompressionRequest` with `IReadOnlyList<string>? ExcludedPaths`.
   - Update `ZstdTarArchiveEngine` and `SharpCompressArchiveEngine` to check `ExcludedPaths` during directory enumeration and omit excluded files and subtrees.
   - Update `MainWindowViewModel.ExecuteCompressAsync` to pass all staged source paths and active exclusions from `CompressionSettingsViewModel`.

3. **Atomic Archive Deletion Pipeline**:
   - Add `ArchiveDeleteRequest` and `ArchiveDeleteResult` models to `RusZip.Core.Models`.
   - Add `Task<ArchiveDeleteResult> DeleteEntriesAsync(...)` to `IArchiveEngine`, implemented across `ZstdTarArchiveEngine`, `SharpCompressArchiveEngine`, and `UnifiedArchiveEngine`.
   - For `.zrus` and `.zip` archives, deletion reads existing entries, streams all non-deleted entries into a temporary file (`<ARCHIVE>.tmp.<GUID>`), and replaces the original archive on completion.
   - Read-only formats (`.rar`, `.7z`, `.gz`, `.tar.gz`) throw `NotSupportedException`.

4. **Archive Browser Modification Workflows**:
   - In `ArchiveBrowserView` and `ArchiveBrowserViewModel`:
     - Add **"Add to Archive" / "Append Files..."** toolbar & context menu action (calling `IArchiveEngine.AppendAsync` with multi-selected files/folders).
     - Add **"Delete Selected"** toolbar & context menu action with confirmation dialog (calling `IArchiveEngine.DeleteEntriesAsync`).
     - Actions are dynamically enabled only for compressible formats (`ArchiveFormatRegistry.IsCompressibleExtension`).
     - Support multi-selection (`SelectionMode="Multiple"`) for batch extraction and batch deletion.

## Technical Architecture & Code Contracts

### 1. Request Models in `RusZip.Core.Models`
```csharp
// In src/RusZip.Core/Models/ArchiveRequests.cs
public sealed record ArchiveCompressionRequest(
    IReadOnlyList<string> SourcePaths,
    string DestinationArchivePath,
    int CompressionLevel = 9,
    string? BaseDirectory = null,
    IReadOnlyList<string>? ExcludedPaths = null
)
{
    public ArchiveCompressionRequest(string sourcePath, string destinationArchivePath, int compressionLevel = 9)
        : this([sourcePath], destinationArchivePath, compressionLevel) { }

    public string SourcePath => SourcePaths.Count > 0 ? SourcePaths[0] : string.Empty;
}

public sealed record ArchiveDeleteRequest(
    string ArchivePath,
    IReadOnlyList<string> EntryPaths,
    int CompressionLevel = 9
);

public sealed record ArchiveDeleteResult(
    bool Success,
    string ArchivePath,
    int DeletedEntriesCount,
    int RetainedEntriesCount,
    long UncompressedBytes,
    long CompressedBytes,
    long ElapsedMilliseconds
);
```

### 2. `IArchiveEngine` Interface in `RusZip.Core.Abstractions`
```csharp
// In src/RusZip.Core/Abstractions/IArchiveEngine.cs
public interface IArchiveEngine
{
    Task CompressAsync(
        ArchiveCompressionRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default);

    Task<AppendResult> AppendAsync(
        ArchiveAppendRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default);

    Task<ArchiveDeleteResult> DeleteEntriesAsync(
        ArchiveDeleteRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default);

    Task<ExtractionResult> ExtractAsync(
        ArchiveExtractionRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        CancellationToken ct = default);
}
```

### 3. Desktop Staging Model in `RusZip.Desktop.ViewModels`
```csharp
// In src/RusZip.Desktop/ViewModels/StagedSourceItemViewModel.cs
public partial class StagedSourceItemViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _fullPath = string.Empty;
    [ObservableProperty] private string _relativePath = string.Empty;
    [ObservableProperty] private bool _isDirectory;
    [ObservableProperty] private long _size;
    [ObservableProperty] private DateTimeOffset? _lastModified;
    [ObservableProperty] private string _attributes = string.Empty;
    [ObservableProperty] private bool _isExcluded;
    [ObservableProperty] private bool _isExpanded;

    public ObservableCollection<StagedSourceItemViewModel> Children { get; } = [];
    public StagedSourceItemViewModel? Parent { get; set; }
}
```

## Consequences
- Gives users full control to build, inspect, and refine multi-source compression jobs directly within the GUI.
- Prevents packing unwanted files (e.g. build artifacts, OS thumbnails) via fine-grained exclusions.
- Provides complete read/write/delete lifecycle for `.zrus` and `.zip` archives.
- Maintains atomic crash safety across all compression, append, and deletion operations via `.tmp.<GUID>` streaming pipelines.

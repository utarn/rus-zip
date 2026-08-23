# 0009: File Associations, Shell Integration, and Conflict Resolution

We decided on a flat root OS context menu integration, an interactive extraction conflict resolution engine seam, multi-platform file association management with recurring startup prompts, and a single-instance IPC coordinator for desktop opening.

## Context

Users opening archive files from Windows Explorer, macOS Finder, or Linux file managers expect native system integration:
1. **Shell Verbs**: Right-click actions (`Extract here`, `Extract to...`, `Extract to subfolder`) directly in the file manager context menu.
2. **File Associations**: Automatic default handler registration and user prompts if archive extensions (`.zrus`, `.zip`, `.tar.gz`, `.tgz`, `.7z`, `.rar`, `.gz`) are unassociated or claimed by third-party applications.
3. **Collision Handling**: Interactive prompts (`Yes`, `Yes to all`, `No`, `No to all`, `Cancel`) when extracting files that already exist at the destination, with metadata comparison.
4. **Collision-free Subfolder Extraction**: `Extract to subfolder` automatically computes clean folder names (stripping compound extensions like `.tar.gz` ➔ `archive/`) and increments numerical suffixes (`archive_2/`, `archive_3/`) when directory collisions exist.
5. **Process Lifecycle**: Single-instance desktop window activation when double-clicking archives via IPC, while context-menu extractions execute in lightweight independent quick-extract windows.

---

## Decision

### 1. Interactive Conflict Resolution in Core Engine (`RusZip.Core`)

We extend `RusZip.Core` with an interactive and policy-driven conflict resolution abstraction (`IFileConflictResolver`).

#### Contracts (`src/RusZip.Core/Abstractions/IFileConflictResolver.cs`, `src/RusZip.Core/Models/FileConflictModels.cs`):

```csharp
namespace RusZip.Core.Abstractions;

public enum FileConflictResolution
{
    Overwrite,
    OverwriteAll,
    Skip,
    SkipAll,
    Abort
}

public sealed record FileConflictContext(
    string TargetPath,
    string RelativeEntryPath,
    long EntryUncompressedSize,
    DateTimeOffset? EntryLastModified,
    long ExistingFileSize,
    DateTimeOffset ExistingLastModified
);

public interface IFileConflictResolver
{
    ValueTask<FileConflictResolution> ResolveConflictAsync(
        FileConflictContext context,
        CancellationToken cancellationToken = default);
}
```

Extend `ArchiveExtractionRequest` (`src/RusZip.Core/Models/ArchiveRequests.cs`):
```csharp
public sealed record ArchiveExtractionRequest(
    string ArchivePath,
    string DestinationDirectory,
    bool Overwrite = true,
    ExtractionLimits? Limits = null,
    IReadOnlyList<string>? Entries = null,
    IFileConflictResolver? ConflictResolver = null
);
```

#### SafeArchiveExtractor Integration (`src/RusZip.Core/Engines/SafeArchiveExtractor.cs`):
- When a destination file already exists:
  - If `ConflictResolver` is provided and the session policy has not been set to `OverwriteAll` or `SkipAll`:
    - Call `await ConflictResolver.ResolveConflictAsync(...)`.
    - `Overwrite` / `OverwriteAll`: Overwrite destination file.
    - `Skip` / `SkipAll`: Omit extraction for this entry, advance progress counter without writing file.
    - `Abort`: Throw `OperationCanceledException` and trigger standard cleanup of newly created files.
  - If `ConflictResolver` is null: Fallback to existing `overwrite` flag (throw `IOException` if `overwrite == false`).

---

### 2. Compound Extension Stripping & Auto-Suffixed Folder Naming (`RusZip.Core`)

Add extraction folder resolver (`src/RusZip.Core/Utils/ExtractionPathResolver.cs`):
- Resolves base archive directory name by matching against `ArchiveFormatRegistry.Formats` (`.tar.gz` ➔ stripped to base name instead of leaving `.tar`).
- When extracting to subfolder: checks `Directory.Exists(targetPath)`. If present, probes `_2`, `_3`, ... until an unused directory is found.

```csharp
public static class ExtractionPathResolver
{
    public static string GetArchiveBaseName(string archivePath)
    {
        var fileName = Path.GetFileName(archivePath);
        if (ArchiveFormatRegistry.TryDetect(archivePath, out var descriptor))
        {
            foreach (var ext in descriptor.Extensions.OrderByDescending(e => e.Length))
            {
                if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    return fileName[..^ext.Length];
                }
            }
        }
        return Path.GetFileNameWithoutExtension(fileName);
    }

    public static string ResolveUniqueDestinationDirectory(string parentDirectory, string baseName)
    {
        var primaryPath = Path.Combine(parentDirectory, baseName);
        if (!Directory.Exists(primaryPath) && !File.Exists(primaryPath))
        {
            return primaryPath;
        }

        int suffix = 2;
        while (true)
        {
            var candidate = Path.Combine(parentDirectory, $"{baseName}_{suffix}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
            suffix++;
        }
    }
}
```

---

### 3. Windows Shell Context Menu Registration (`RusZip.Desktop`)

On Windows, register flat context menu entries directly under `HKCU\Software\Classes\SystemFileAssociations\<ext>\shell\` and per-extension ProgID keys:

- `HKCU\Software\Classes\SystemFileAssociations\<ext>\shell\RusZip.ExtractHere`:
  - `(Default)` = "Extract here"
  - `Icon` = `"<PathToExe>"`
  - `command\(Default)` = `"<PathToExe>" --extract-here "%1"`
- `HKCU\Software\Classes\SystemFileAssociations\<ext>\shell\RusZip.ExtractTo`:
  - `(Default)` = "Extract to..."
  - `Icon` = `"<PathToExe>"`
  - `command\(Default)` = `"<PathToExe>" --extract-to "%1"`
- `HKCU\Software\Classes\SystemFileAssociations\<ext>\shell\RusZip.ExtractToSubfolder`:
  - `(Default)` = "Extract to subfolder"
  - `Icon` = `"<PathToExe>"`
  - `command\(Default)` = `"<PathToExe>" --extract-to-dir "%1"`

Also registers ProgIDs (`RusZip.<ext>`) with `Capabilities\FileAssociations` under `HKCU\Software\RusZip` and `HKCU\Software\RegisteredApplications` to integrate with Windows 10/11 Default Apps.

---

### 4. Linux and macOS Integration (`RusZip.Desktop`)

- **Linux**:
  - Writes `~/.local/share/applications/rus-zip.desktop` with FreeDesktop actions:
    ```desktop
    [Desktop Entry]
    Type=Application
    Name=RusZip
    Exec=rus-zip %F
    Icon=rus-zip
    MimeType=application/zip;application/x-tar;application/gzip;application/x-7z-compressed;application/vnd.rar;application/x-zstd-tar;
    Actions=ExtractHere;ExtractTo;ExtractToSubfolder;

    [Desktop Action ExtractHere]
    Name=Extract here
    Exec=rus-zip --extract-here %f

    [Desktop Action ExtractTo]
    Name=Extract to...
    Exec=rus-zip --extract-to %f

    [Desktop Action ExtractToSubfolder]
    Name=Extract to subfolder
    Exec=rus-zip --extract-to-dir %f
    ```
  - Registers MIME defaults using `xdg-mime default rus-zip.desktop <mimetypes>`.
- **macOS**:
  - Registers document types in `Info.plist` for `.app` bundle.
  - Registers UTIs via LaunchServices (`LSSetDefaultRoleHandlerForContentType`).
  - Hooks Avalonia `IClassicDesktopStyleApplicationLifetime` / file open events for archive opening.

---

### 5. File Association Service & Startup Prompt (`RusZip.Desktop`)

- `IFileAssociationService` (`src/RusZip.Desktop/Services/IFileAssociationService.cs`):
  - Checks whether supported extensions (`.zrus`, `.zip`, `.tar.gz`, `.tgz`, `.7z`, `.rar`, `.gz`) are assigned to RusZip.
  - On startup: If any supported extension is unassociated or owned by another app, display `FileAssociationPromptDialog`.
  - Prompt features:
    - Checklist of all supported extensions (checked by default).
    - "Set as Default" button (applies associations and opens Windows Default Apps if necessary).
    - "Not Now" button (dismisses for this session; will check again on subsequent launches as requested).

---

### 6. Quick Extract Modal Window (`RusZip.Desktop`)

- CLI extraction flags (`--extract-here`, `--extract-to`, `--extract-to-dir`):
  - Open `QuickExtractWindow` (`src/RusZip.Desktop/Views/QuickExtractWindow.axaml`).
  - Driven by `QuickExtractViewModel`:
    - Shows progress bar, current file, throughput rate, bytes processed, and Cancel button.
    - If collisions occur, pops `FileConflictDialog` with file metadata comparison (`Yes`, `Yes to all`, `No`, `No to all`, `Cancel`).
    - On success: displays extracted summary metrics, an **Open Folder** button, a **Close** button, and auto-dismisses after countdown.

---

### 7. Single-Instance IPC Coordinator (`RusZip.Desktop`)

- Architecture (`src/RusZip.Desktop/Services/SingleInstanceCoordinator.cs`):
  - Uses Named Pipe on Windows (`\\.\pipe\RusZip_SingleInstance_<User>`) and Unix Domain Socket on Linux/macOS (`/tmp/ruszip_<User>.sock`).
  - When double-clicking an archive file (`rus-zip <archivePath>`):
    - Client attempts connection to existing instance.
    - If found: sends archive file path, existing instance brings `MainWindow` to front and opens archive, client process exits immediately.
    - If not found: binds pipe server, runs `MainWindow`, listens for future open requests.
  - Quick-extract CLI flags bypass IPC to run as independent, fast-executing tasks.

---

## Consequences

- Direct flat context menu entries on Windows Explorer and Linux file managers for all archive types.
- Complete safety against silent overwrites via interactive conflict resolution with full comparison metadata.
- Clean directory hierarchy creation for compound archives (`.tar.gz`).
- Predictable single-instance UX when browsing archives from desktop file managers.

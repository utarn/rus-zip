# 0016: Incremental Compression Against Baseline Archives

We decided to support incremental compression for `.zrus` archives where users can provide one or more baseline `.zrus` archives alongside candidate source files, producing a new differential `.zrus` archive containing only new and modified files evaluated through a multi-tiered timestamp and size resolution algorithm.

## Context
Full backup and compression of large directories or project workspaces is time- and bandwidth-intensive. Users need the ability to perform incremental backups by supplying:
1. One or more previous baseline `.zrus` archives.
2. The current filesystem directory or file set.
3. A target destination path for the differential `.zrus` archive.

The system must resolve the authoritative "latest version" of each file across all baseline archives using:
- The entry's internal modification timestamp (`ArchiveEntry.LastModified` / `TarEntry.ModificationTime`).
- In the event of a tie, the baseline `.zrus` archive file's filesystem timestamp (`FileInfo.LastWriteTimeUtc`).
- In the event of identical archive timestamps, the order of baseline arguments.

## Decision

### 1. Multi-Tiered Baseline Indexing & Resolution Model
In [`src/RusZip.Core/Models/BaselineEntryInfo.cs`](../../src/RusZip.Core/Models/BaselineEntryInfo.cs) and [`src/RusZip.Core/Engines/BaselineArchiveIndex.cs`](../../src/RusZip.Core/Engines/BaselineArchiveIndex.cs):

```csharp
namespace RusZip.Core.Engines;

public sealed record BaselineEntryInfo(
    string RelativePath,
    DateTimeOffset EntryLastModified,
    DateTimeOffset ArchiveFileModified,
    long UncompressedSize,
    string SourceArchivePath,
    int ArgumentIndex
);

public sealed class BaselineArchiveIndex
{
    private readonly Dictionary<string, BaselineEntryInfo> _entries = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, BaselineEntryInfo> Entries => _entries;

    public static async Task<BaselineArchiveIndex> BuildAsync(
        IReadOnlyList<string> baselineArchivePaths,
        CancellationToken ct = default)
    {
        var index = new BaselineArchiveIndex();

        for (int i = 0; i < baselineArchivePaths.Count; i++)
        {
            var archivePath = Path.GetFullPath(baselineArchivePaths[i]);
            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException($"Baseline archive does not exist: {archivePath}", archivePath);
            }

            var archiveFileInfo = new FileInfo(archivePath);
            var archiveModified = archiveFileInfo.LastWriteTimeUtc;

            // Stream Tar headers only (zero payload disk caching)
            await using var fileStream = File.OpenRead(archivePath);
            await using var zstdStream = new ZstdSharp.DecompressionStream(fileStream);
            using var tarReader = new System.Formats.Tar.TarReader(zstdStream);

            while (await tarReader.GetNextEntryAsync(copyData: false, cancellationToken: ct) is { } entry)
            {
                if (entry.EntryType == System.Formats.Tar.TarEntryType.Directory)
                {
                    continue;
                }

                var normalizedPath = entry.Name.Replace('\\', '/').Trim('/');
                var candidate = new BaselineEntryInfo(
                    RelativePath: normalizedPath,
                    EntryLastModified: entry.ModificationTime,
                    ArchiveFileModified: archiveModified,
                    UncompressedSize: entry.Length,
                    SourceArchivePath: archivePath,
                    ArgumentIndex: i
                );

                if (!index._entries.TryGetValue(normalizedPath, out var current))
                {
                    index._entries[normalizedPath] = candidate;
                }
                else
                {
                    // Multi-tiered comparison hierarchy:
                    // 1. Entry Last Modified (newer wins)
                    // 2. Archive File Last Modified (newer wins)
                    // 3. Argument Index (later argument wins)
                    if (candidate.EntryLastModified > current.EntryLastModified)
                    {
                        index._entries[normalizedPath] = candidate;
                    }
                    else if (candidate.EntryLastModified == current.EntryLastModified)
                    {
                        if (candidate.ArchiveFileModified > current.ArchiveFileModified)
                        {
                            index._entries[normalizedPath] = candidate;
                        }
                        else if (candidate.ArchiveFileModified == current.ArchiveFileModified &&
                                 candidate.ArgumentIndex > current.ArgumentIndex)
                        {
                            index._entries[normalizedPath] = candidate;
                        }
                    }
                }
            }
        }

        return index;
    }

    public bool ShouldIncludeFile(string relativePath, DateTimeOffset sourceLastModified, long sourceLength)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (!_entries.TryGetValue(normalized, out var baseline))
        {
            return true; // New file
        }

        if (sourceLastModified > baseline.EntryLastModified)
        {
            return true; // Modified (strictly newer)
        }

        if (sourceLastModified == baseline.EntryLastModified && sourceLength != baseline.UncompressedSize)
        {
            return true; // Modified (size mismatch)
        }

        return false; // Up to date
    }
}
```

### 2. Request Model Integration
In [`src/RusZip.Core/Models/ArchiveRequests.cs`](../../src/RusZip.Core/Models/ArchiveRequests.cs):
```csharp
public sealed record ArchiveCompressionRequest(
    IReadOnlyList<string> SourcePaths,
    string DestinationArchivePath,
    int CompressionLevel = 9,
    string? BaseDirectory = null,
    IReadOnlyCollection<string>? ExcludedPaths = null,
    string? Password = null,
    long? SplitSizeBytes = null,
    IReadOnlyList<string>? BaselineArchivePaths = null
);
```

### 3. Compression Engine Diff Execution
In [`src/RusZip.Core/Engines/ZstdTarArchiveEngine.cs`](../../src/RusZip.Core/Engines/ZstdTarArchiveEngine.cs):
- If `request.BaselineArchivePaths is { Count: > 0 }`:
  1. Build `BaselineArchiveIndex` asynchronously from the baseline archives.
  2. While scanning source directories/files, filter candidate files using `index.ShouldIncludeFile(relPath, fileInfo.LastWriteTimeUtc, fileInfo.Length)`.
  3. If 0 files changed and `--allow-empty` is not specified, return without creating an empty file and report completion with 0 files.
  4. Otherwise, stream the filtered diff files into the target `.zrus` archive.

### 4. CLI Interface
In [`src/RusZip.Cli/Commands/CompressCommand.cs`](../../src/RusZip.Cli/Commands/CompressCommand.cs):
- Add `-b, --baseline <PATHS...>`:
  ```bash
  # Incremental backup against a single baseline
  rus-zip compress ./src -o src_diff1.zrus -b src_base.zrus

  # Incremental backup across multiple baseline generations
  rus-zip c ./src -o src_diff2.zrus -b src_base.zrus src_diff1.zrus
  ```
- Output summary prints:
  `Incremental diff: 34 files changed (12 new, 22 modified, 410 skipped).`

### 5. Desktop GUI Staging Grid Integration
In [`src/RusZip.Desktop/ViewModels/CompressionSettingsViewModel.cs`](../../src/RusZip.Desktop/ViewModels/CompressionSettingsViewModel.cs):
- Add `IsIncremental`, `BaselineArchives` (ObservableCollection of file paths), and `BaselineDiffSummary` status (`"14 modified, 3 new, 280 unchanged"`).

## Consequences
- Enables efficient incremental backups saving CPU, storage, and transfer time.
- Deterministic, unambiguous version resolution across multiple baseline archives.
- Seamless compatibility with password protection (`-p`) and volume splitting (`-s`).

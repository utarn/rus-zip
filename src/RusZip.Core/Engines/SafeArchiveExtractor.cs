using System.Buffers;
using System.Security;
using RusZip.Core.Models;

namespace RusZip.Core.Engines;

public static class SafeArchiveExtractor
{
    public const int BufferSize = 81920; // 80 KB

    /// <summary>
    /// Extracts all entries from <paramref name="source"/> into <paramref name="destinationDirectory"/>.
    /// </summary>
    /// <remarks>
    /// Partial-cleanup semantics: if extraction aborts with a <see cref="SecurityException"/> (a malicious
    /// entry name or a symlinked/reparse-point path component under the destination), any files and
    /// directories that this invocation created are removed best-effort before the exception propagates.
    /// Cleanup is best-effort only: paths that pre-existed before the call are left untouched (an
    /// overwritten pre-existing file cannot be restored and is not deleted), and a file that is locked or
    /// in use may survive cleanup.
    /// </remarks>
    public static async Task ExtractAllAsync(
        IArchiveExtractionSource source,
        string destinationDirectory,
        bool overwrite,
        long totalBytes,
        IProgress<ProgressReport>? progress,
        CancellationToken ct = default)
    {
        var destDir = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destDir);

        var normalizedDestDir = destDir.EndsWith(Path.DirectorySeparatorChar)
            ? destDir
            : destDir + Path.DirectorySeparatorChar;

        long processedBytes = 0;
        int processedFiles = 0;
        var extractedDirectories = new List<(string TargetPath, DateTimeOffset? ModTime, UnixFileMode? Mode)>();
        var createdPaths = new List<string>();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            await foreach (var entry in source.ReadEntriesAsync(ct))
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(entry.RelativePath))
                    continue;

                var entryName = entry.RelativePath.Replace('\\', '/');
                var targetPath = ResolveAndValidateTargetPath(destDir, normalizedDestDir, entryName, entry.RelativePath);

                // Refuse to write through a symlinked/reparse-point path component under destDir.
                EnsureNoSymlinkedPathComponents(destDir, targetPath);

                // 2. Directory handling
                if (entry.IsDirectory || entryName.EndsWith('/') || Directory.Exists(targetPath))
                {
                    EnsureDirectoryExists(targetPath, createdPaths);
                    if (entry.ModificationTime.HasValue || entry.UnixMode.HasValue)
                    {
                        extractedDirectories.Add((targetPath, entry.ModificationTime, entry.UnixMode));
                    }
                    continue;
                }

                // 3. Parent directory creation
                var parentDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    EnsureDirectoryExists(parentDir, createdPaths);
                }

                // 4. Overwrite check
                if (!overwrite && File.Exists(targetPath))
                {
                    throw new IOException($"Destination file already exists and overwrite is false: '{targetPath}'");
                }

                // 5. Stream writing with buffer pooling & progress reporting
                var fileExistedBefore = File.Exists(targetPath);
                await using (var entryStream = await entry.OpenStreamAsync(ct))
                await using (var outFs = new FileStream(
                    targetPath,
                    overwrite ? FileMode.Create : FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    useAsync: true))
                {
                    int bytesRead;
                    while ((bytesRead = await entryStream.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
                    {
                        await outFs.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        processedBytes += bytesRead;

                        progress?.Report(new ProgressReport(
                            ProcessedBytes: processedBytes,
                            TotalBytes: totalBytes,
                            CurrentFileName: entryName,
                            Percentage: totalBytes > 0 ? (double)processedBytes / totalBytes * 100.0 : 0,
                            ProcessedFiles: processedFiles,
                            IsIndeterminate: totalBytes <= 0
                        ));
                    }
                }

                if (!fileExistedBefore)
                    createdPaths.Add(targetPath);

                processedFiles++;

                // 6. Restore file metadata
                if (entry.ModificationTime.HasValue)
                {
                    File.SetLastWriteTimeUtc(targetPath, entry.ModificationTime.Value.UtcDateTime);
                }
                if (!OperatingSystem.IsWindows() && entry.UnixMode.HasValue && entry.UnixMode.Value != 0 && entry.UnixMode.Value != (UnixFileMode)(-1))
                {
                    File.SetUnixFileMode(targetPath, entry.UnixMode.Value);
                }
            }

            // 7. Restore directory metadata in bottom-up order (deepest directories first)
            foreach (var dir in extractedDirectories.OrderByDescending(d => d.TargetPath.Length))
            {
                if (Directory.Exists(dir.TargetPath))
                {
                    try
                    {
                        if (dir.ModTime.HasValue)
                        {
                            Directory.SetLastWriteTimeUtc(dir.TargetPath, dir.ModTime.Value.UtcDateTime);
                        }
                        if (!OperatingSystem.IsWindows() && dir.Mode.HasValue && dir.Mode.Value != 0 && dir.Mode.Value != (UnixFileMode)(-1))
                        {
                            File.SetUnixFileMode(dir.TargetPath, dir.Mode.Value);
                        }
                    }
                    catch
                    {
                        // Best-effort directory metadata restoration
                    }
                }
            }
        }
        catch (SecurityException)
        {
            CleanupCreatedPaths(createdPaths);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Resolves the absolute target path for an entry and validates it stays inside <paramref name="destDir"/>.
    /// </summary>
    /// <remarks>
    /// Three layers of defense:
    /// 1. Lexical component rejection: any path component ending in '.' or ' ' (excluding '.'/'..' themselves)
    ///    is rejected. On Windows, Win32 normalizes trailing dots/spaces, so ".. " resolves to the parent
    ///    directory and "file." to "file"; on Unix these become literal, confusingly-named entries. Rejecting
    ///    them everywhere keeps behavior consistent and safe on all operating systems.
    /// 2. Prefix containment check (legacy) against the normalized destination root.
    /// 3. Defense-in-depth containment re-check using <see cref="Path.GetRelativePath"/>: the relative path
    ///    must not be rooted and must not begin with a ".." path segment.
    /// </remarks>
    private static string ResolveAndValidateTargetPath(string destDir, string normalizedDestDir, string entryName, string originalPath)
    {
        // 1. Reject path components ending in '.' or ' ' (trailing dots/spaces are normalized by
        //    Win32 into traversal or name collisions; rejecting them uniformly on all OSes is safest).
        var components = entryName.Split('/');
        foreach (var component in components)
        {
            if (component.Length == 0 || component == "." || component == "..")
                continue;

            if (component[^1] is '.' or ' ')
            {
                throw new SecurityException($"Malicious entry detected. Malicious path traversal detected in archive entry: {originalPath}");
            }
        }

        var targetPath = Path.GetFullPath(Path.Combine(destDir, entryName));

        // 2. Path traversal security check (lexical prefix)
        if (!targetPath.StartsWith(normalizedDestDir, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(targetPath, destDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"Malicious entry detected. Malicious path traversal detected in archive entry: {originalPath}");
        }

        // 3. Defense in depth: re-verify containment via Path.GetRelativePath. The relative path must
        //    not be rooted (e.g. different drive) and must not begin with a ".." path segment.
        var relativePath = Path.GetRelativePath(destDir, targetPath);
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new SecurityException($"Malicious entry detected. Malicious path traversal detected in archive entry: {originalPath}");
        }

        return targetPath;
    }

    /// <summary>
    /// Walks every path component of <paramref name="targetPath"/> below <paramref name="destDir"/> and
    /// rejects the write if any existing component is a symlink or reparse point. Prevents a hostile
    /// pre-seeded destination (e.g. <c>dest/evil → /etc</c>) from redirecting writes outside the destination.
    /// </summary>
    private static void EnsureNoSymlinkedPathComponents(string destDir, string targetPath)
    {
        var relative = Path.GetRelativePath(destDir, targetPath);
        if (relative == ".")
            return;

        var current = destDir;
        foreach (var component in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if (IsSymlinkOrReparsePoint(current))
            {
                throw new SecurityException($"Malicious entry detected. Refusing to extract through symlinked path component: {current}");
            }
        }
    }

    private static bool IsSymlinkOrReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates <paramref name="path"/> and any missing ancestor directories, tracking each directory that
    /// this call actually created so it can be cleaned up if extraction aborts.
    /// </summary>
    private static void EnsureDirectoryExists(string path, List<string> createdPaths)
    {
        if (Directory.Exists(path))
            return;

        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
            EnsureDirectoryExists(parent, createdPaths);

        Directory.CreateDirectory(path);
        createdPaths.Add(path);
    }

    /// <summary>
    /// Best-effort removal of paths created by this extraction invocation. Deepest paths are removed first
    /// so parent directories are emptied before being deleted.
    /// </summary>
    private static void CleanupCreatedPaths(List<string> createdPaths)
    {
        foreach (var path in createdPaths.OrderByDescending(p => p.Length))
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup; ignore failures (locked/in-use files may survive).
            }
        }
    }
}

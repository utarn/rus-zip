using System.Buffers;
using System.Security;
using RusZip.Core.Models;

namespace RusZip.Core.Engines;

public static class SafeArchiveExtractor
{
    public const int BufferSize = 81920; // 80 KB

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
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            await foreach (var entry in source.ReadEntriesAsync(ct))
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(entry.RelativePath))
                    continue;

                var entryName = entry.RelativePath.Replace('\\', '/');
                var targetPath = Path.GetFullPath(Path.Combine(destDir, entryName));

                // 1. Path traversal security check
                if (!targetPath.StartsWith(normalizedDestDir, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(targetPath, destDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityException($"Malicious entry detected. Malicious path traversal detected in archive entry: {entry.RelativePath}");
                }

                // 2. Directory handling
                if (entry.IsDirectory || entryName.EndsWith('/') || Directory.Exists(targetPath))
                {
                    Directory.CreateDirectory(targetPath);
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
                    Directory.CreateDirectory(parentDir);
                }

                // 4. Overwrite check
                if (!overwrite && File.Exists(targetPath))
                {
                    throw new IOException($"Destination file already exists and overwrite is false: '{targetPath}'");
                }

                // 5. Stream writing with buffer pooling & progress reporting
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
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

using RusZip.Core.Models;

namespace RusZip.Core.Utils;

/// <summary>
/// Utility methods for resolving archive base names and computing collision-free extraction directories.
/// </summary>
public static class ExtractionPathResolver
{
    /// <summary>
    /// Gets the base name of an archive by detecting and stripping known single or compound archive extensions.
    /// Handles multi-part extensions (e.g. '.tar.gz', '.tgz') and filenames with dots (e.g. 'release-v1.0.0.tar.gz').
    /// Falls back to stripping the standard single extension if not detected in the format registry.
    /// </summary>
    /// <param name="archivePath">The path or filename of the archive.</param>
    /// <returns>The base name of the archive without its archive extension.</returns>
    public static string GetArchiveBaseName(string? archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return string.Empty;
        }

        var normalized = archivePath.Trim().Replace('\\', '/').TrimEnd('/');
        var fileName = Path.GetFileName(normalized);
        if (string.IsNullOrEmpty(fileName))
        {
            return string.Empty;
        }

        if (ArchiveFormatRegistry.TryDetect(fileName, out var descriptor))
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

    /// <summary>
    /// Resolves a collision-free destination directory path within the specified parent directory.
    /// If the primary path exists (as a directory or file), appends incremental numerical suffixes
    /// ('_2', '_3', ...) until an unused path is found.
    /// </summary>
    /// <param name="parentDirectory">The parent directory where extraction will occur.</param>
    /// <param name="baseName">The base directory name.</param>
    /// <returns>A unique, unused directory path.</returns>
    public static string ResolveUniqueDestinationDirectory(string parentDirectory, string baseName)
    {
        ArgumentNullException.ThrowIfNull(parentDirectory);
        ArgumentNullException.ThrowIfNull(baseName);

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

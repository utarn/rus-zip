using System.Text.RegularExpressions;
using RusZip.Core.Models;

namespace RusZip.Core.Engines;

/// <summary>
/// Handles naming, resolution, and discovery of sequential multi-volume archive parts.
/// Supports canonical infix formatting: &lt;base&gt;.part&lt;N&gt;.&lt;ext&gt; (e.g. backup.part1.zrus)
/// and zero-padded variants (.part01, .part001).
/// </summary>
public static partial class VolumeNameResolver
{
    private static readonly Regex PartPattern = new(
        @"(?<prefix>.*)\.part(?<number>\d+)(?<ext>\..+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Checks if a file path is a split volume part.
    /// </summary>
    public static bool TryParseVolumePath(
        string path,
        out string basePath,
        out string extension,
        out int partIndex,
        out int paddingLength)
    {
        basePath = string.Empty;
        extension = string.Empty;
        partIndex = 0;
        paddingLength = 0;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        var dir = Path.GetDirectoryName(path) ?? string.Empty;

        var match = PartPattern.Match(fileName);
        if (!match.Success)
        {
            return false;
        }

        var prefix = match.Groups["prefix"].Value;
        var numStr = match.Groups["number"].Value;
        var ext = match.Groups["ext"].Value;

        if (!int.TryParse(numStr, out partIndex) || partIndex < 1)
        {
            return false;
        }

        paddingLength = numStr.Length > 1 && numStr.StartsWith('0') ? numStr.Length : 0;
        basePath = string.IsNullOrEmpty(dir) ? prefix : Path.Combine(dir, prefix);
        extension = ext;
        return true;
    }

    /// <summary>
    /// Checks if the specified file path is formatted as a multi-volume part or has existing split siblings.
    /// </summary>
    public static bool IsMultiVolume(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (TryParseVolumePath(path, out _, out _, out _, out _)) return true;

        var (basePath, extension) = SplitBaseAndExtension(path);
        var part1Path = $"{basePath}.part1{extension}";
        var part01Path = $"{basePath}.part01{extension}";
        return File.Exists(part1Path) || File.Exists(part01Path);
    }

    /// <summary>
    /// Formats the target volume part file path.
    /// </summary>
    public static string GetVolumePath(string destinationPath, int partIndex, int paddingLength = 0)
    {
        var (basePath, extension) = SplitBaseAndExtension(destinationPath);
        var numStr = paddingLength > 1 ? partIndex.ToString().PadLeft(paddingLength, '0') : partIndex.ToString();
        return $"{basePath}.part{numStr}{extension}";
    }

    /// <summary>
    /// Splits an archive path into its base path and compound extension.
    /// </summary>
    public static (string BasePath, string Extension) SplitBaseAndExtension(string path)
    {
        if (TryParseVolumePath(path, out var basePart, out var extPart, out _, _))
        {
            return (basePart, extPart);
        }

        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileName(path);

        // Check compound extensions in registry
        if (ArchiveFormatRegistry.TryDetect(fileName, out var descriptor))
        {
            foreach (var ext in descriptor.Extensions)
            {
                if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    var baseName = fileName[..^ext.Length];
                    return (string.IsNullOrEmpty(dir) ? baseName : Path.Combine(dir, baseName), ext);
                }
            }
        }

        var standardExt = Path.GetExtension(path);
        var standardBase = Path.GetFileNameWithoutExtension(path);
        return (string.IsNullOrEmpty(dir) ? standardBase : Path.Combine(dir, standardBase), standardExt);
    }

    /// <summary>
    /// Discovers all sequential volume files starting from part 1, ensuring continuity.
    /// Throws <see cref="MissingVolumeException"/> if a gap in the volume sequence is detected.
    /// </summary>
    public static IReadOnlyList<string> DiscoverVolumeSequence(string startingFilePath)
    {
        var fullPath = Path.GetFullPath(startingFilePath);
        var (basePath, extension) = SplitBaseAndExtension(fullPath);
        var dir = Path.GetDirectoryName(fullPath) ?? string.Empty;

        int paddingLength = 0;
        if (TryParseVolumePath(fullPath, out _, _, out _, out var pad))
        {
            paddingLength = pad;
        }

        // Determine if part1 or part01 or part001 is used
        string part1Candidate = GetVolumePath(fullPath, 1, paddingLength);
        if (!File.Exists(part1Candidate))
        {
            // Try unpadded or other paddings
            if (File.Exists(GetVolumePath(fullPath, 1, 0)))
            {
                paddingLength = 0;
                part1Candidate = GetVolumePath(fullPath, 1, 0);
            }
            else if (File.Exists(GetVolumePath(fullPath, 1, 2)))
            {
                paddingLength = 2;
                part1Candidate = GetVolumePath(fullPath, 1, 2);
            }
            else if (File.Exists(GetVolumePath(fullPath, 1, 3)))
            {
                paddingLength = 3;
                part1Candidate = GetVolumePath(fullPath, 1, 3);
            }
        }

        if (!File.Exists(part1Candidate))
        {
            // If the starting file itself exists and is not split, return single file
            if (File.Exists(fullPath) && !TryParseVolumePath(fullPath, out _, out _, out _, out _))
            {
                return [fullPath];
            }

            throw new MissingVolumeException($"Initial volume part 1 is missing: expected '{part1Candidate}'.", part1Candidate, 1);
        }

        // Find max existing volume index in directory to detect gaps
        int highestPartFound = 1;
        if (Directory.Exists(dir))
        {
            var prefix = Path.GetFileName(basePath);
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var fname = Path.GetFileName(f);
                if (TryParseVolumePath(fname, out var fbase, out var fext, out var fidx, _) &&
                    string.Equals(fbase, prefix, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(fext, extension, StringComparison.OrdinalIgnoreCase))
                {
                    if (fidx > highestPartFound)
                    {
                        highestPartFound = fidx;
                    }
                }
            }
        }

        var volumes = new List<string>();
        for (int i = 1; i <= highestPartFound; i++)
        {
            var expectedPath = GetVolumePath(fullPath, i, paddingLength);
            if (!File.Exists(expectedPath))
            {
                // Also check unpadded/padded alternative before failing
                var altPath = GetVolumePath(fullPath, i, 0);
                if (File.Exists(altPath))
                {
                    expectedPath = altPath;
                }
                else
                {
                    throw new MissingVolumeException(
                        $"Volume part {i} is missing: expected '{expectedPath}'.",
                        expectedPath,
                        i);
                }
            }

            volumes.Add(expectedPath);
        }

        return volumes;
    }
}

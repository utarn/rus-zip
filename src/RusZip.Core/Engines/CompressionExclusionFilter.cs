namespace RusZip.Core.Engines;

/// <summary>
/// Evaluates request-level exclusion filters for archive compression.
/// Supports both absolute filesystem paths and relative path segments
/// (e.g. "node_modules", "bin/Debug", "temp/cache.tmp", "secret.key").
/// </summary>
public sealed class CompressionExclusionFilter
{
    private readonly List<string> _absoluteExclusions = [];
    private readonly List<string> _relativePatterns = [];
    private readonly string? _baseDirectory;

    public CompressionExclusionFilter(IReadOnlyCollection<string>? excludedPaths, string? baseDirectory = null)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory) ? null : Path.GetFullPath(baseDirectory);

        if (excludedPaths is null || excludedPaths.Count == 0)
        {
            return;
        }

        foreach (var raw in excludedPaths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmed = raw.Trim();

            if (Path.IsPathRooted(trimmed))
            {
                var full = Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                _absoluteExclusions.Add(full);

                // If it starts with '/' or '\' without a drive colon, also treat as relative pattern
                var relCandidate = trimmed.TrimStart('/', '\\');
                if (!relCandidate.Contains(':'))
                {
                    var normalizedRel = NormalizeRelative(relCandidate);
                    if (normalizedRel.Length > 0 && !_relativePatterns.Contains(normalizedRel, StringComparer.OrdinalIgnoreCase))
                    {
                        _relativePatterns.Add(normalizedRel);
                    }
                }
            }
            else
            {
                if (_baseDirectory is not null)
                {
                    var fullFromBase = Path.GetFullPath(Path.Combine(_baseDirectory, trimmed)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    _absoluteExclusions.Add(fullFromBase);
                }

                var normalized = NormalizeRelative(trimmed);
                if (normalized.Length > 0 && !_relativePatterns.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    _relativePatterns.Add(normalized);
                }
            }
        }
    }

    public bool HasExclusions => _absoluteExclusions.Count > 0 || _relativePatterns.Count > 0;

    public bool IsExcluded(string? fullPath, params string?[] candidateRelativePaths)
    {
        if (!HasExclusions)
        {
            return false;
        }

        // 1. Check absolute filesystem exclusions
        if (_absoluteExclusions.Count > 0 && !string.IsNullOrWhiteSpace(fullPath))
        {
            var normFullPath = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            foreach (var abs in _absoluteExclusions)
            {
                if (normFullPath.Equals(abs, comparison))
                {
                    return true;
                }

                if (normFullPath.StartsWith(abs + Path.DirectorySeparatorChar, comparison) ||
                    normFullPath.StartsWith(abs + Path.AltDirectorySeparatorChar, comparison))
                {
                    return true;
                }
            }
        }

        // 2. Check relative pattern exclusions
        if (_relativePatterns.Count > 0)
        {
            foreach (var candidate in candidateRelativePaths)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var normalizedCandidate = NormalizeRelative(candidate);
                if (normalizedCandidate.Length == 0)
                {
                    continue;
                }

                foreach (var pattern in _relativePatterns)
                {
                    if (MatchesPattern(normalizedCandidate, pattern))
                    {
                        return true;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(fullPath))
            {
                var fileName = Path.GetFileName(fullPath.TrimEnd('/', '\\'));
                if (!string.IsNullOrEmpty(fileName))
                {
                    foreach (var pattern in _relativePatterns)
                    {
                        if (MatchesPattern(fileName, pattern))
                        {
                            return true;
                        }
                    }
                }

                if (_baseDirectory is not null)
                {
                    try
                    {
                        var relFromBase = Path.GetRelativePath(_baseDirectory, fullPath);
                        if (!relFromBase.StartsWith("..", StringComparison.Ordinal))
                        {
                            var normalizedRelBase = NormalizeRelative(relFromBase);
                            foreach (var pattern in _relativePatterns)
                            {
                                if (MatchesPattern(normalizedRelBase, pattern))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore relative path calculation errors
                    }
                }
            }
        }

        return false;
    }

    public static string NormalizeRelative(string path)
    {
        var normalized = path.Replace('\\', '/');
        normalized = normalized.Trim('/');
        while (normalized.StartsWith("./", StringComparison.Ordinal) || normalized.StartsWith("../", StringComparison.Ordinal))
        {
            if (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized[2..].TrimStart('/');
            }
            else if (normalized.StartsWith("../", StringComparison.Ordinal))
            {
                normalized = normalized[3..].TrimStart('/');
            }
        }
        return normalized.TrimEnd('/');
    }

    private static bool MatchesPattern(string candidate, string pattern)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;

        if (candidate.Equals(pattern, comparison))
        {
            return true;
        }

        if (candidate.StartsWith(pattern + "/", comparison))
        {
            return true;
        }

        if (candidate.EndsWith("/" + pattern, comparison))
        {
            return true;
        }

        if (candidate.Contains("/" + pattern + "/", comparison))
        {
            return true;
        }

        return false;
    }
}

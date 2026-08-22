namespace RusZip.Core.Engines;

/// <summary>
/// Selective-extraction matching semantics for <see cref="RusZip.Core.Models.ArchiveExtractionRequest.Entries"/>.
/// </summary>
/// <remarks>
/// <para>
/// An archive entry is extracted when the filter is <see langword="null"/> or empty (extract-all,
/// the existing behavior), or when its normalized relative path matches a filter path. Matching is
/// exact relative-path equality OR directory-prefix match: an entry matches when its normalized
/// path equals a filter path, or starts with a filter path followed by a <c>'/'</c>. The
/// directory-prefix form makes a single directory filter select that directory's entire subtree.
/// </para>
/// <para>
/// Normalization (applied to both the entry path and each filter path): <c>'\'</c> is replaced
/// with <c>'/'</c> and leading/trailing <c>'/'</c> separators are trimmed. This makes a trailing
/// separator on a directory filter optional (<c>"folder/"</c> matches <c>"folder"</c> and its
/// subtree) and handles Windows-style backslash paths. Matching is case-sensitive (ordinal), which
/// is correct for archive entry names that are case-sensitive on the source platform.
/// </para>
/// </remarks>
public static class EntryFilter
{
    /// <summary>
    /// Message thrown when a selective-extraction filter is set but no archive entry matches it.
    /// </summary>
    public const string NoMatchMessage = "No archive entries matched the requested extraction filter.";

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="entryRelativePath"/> passes the
    /// selective-extraction filter and should be extracted; <see langword="false"/> when it is
    /// excluded. A <see langword="null"/> or empty <paramref name="entries"/> filter matches every
    /// (non-empty) entry — preserving extract-all behavior.
    /// </summary>
    public static bool IsMatch(string? entryRelativePath, IReadOnlyList<string>? entries)
    {
        if (entries is null || entries.Count == 0)
            return true;
        if (string.IsNullOrWhiteSpace(entryRelativePath))
            return false;

        var normalizedEntry = Normalize(entryRelativePath);
        if (normalizedEntry.Length == 0)
            return false;

        foreach (var filter in entries)
        {
            if (string.IsNullOrWhiteSpace(filter))
                continue;

            var normalizedFilter = Normalize(filter);
            if (normalizedFilter.Length == 0)
                continue;

            // Exact relative-path match (single file, or the directory entry itself).
            if (string.Equals(normalizedEntry, normalizedFilter, StringComparison.Ordinal))
                return true;

            // Directory-prefix match: the entry lives inside the filter directory's subtree.
            if (normalizedEntry.StartsWith(normalizedFilter + "/", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Normalizes a relative path for filter matching: replaces <c>'\'</c> with <c>'/'</c> and
    /// trims leading/trailing <c>'/'</c> separators.
    /// </summary>
    public static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Trim('/');
    }
}

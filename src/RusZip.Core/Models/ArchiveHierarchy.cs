using System.Text.RegularExpressions;

namespace RusZip.Core.Models;

public sealed class ArchiveTreeNode
{
    public required string Name { get; init; }
    public required string RelativePath { get; init; }
    public bool IsDirectory { get; set; }
    public long UncompressedSize { get; set; }
    public long? CompressedSize { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public string Attributes { get; set; } = string.Empty;
    public List<ArchiveTreeNode> Children { get; } = [];
    public bool HasChildren => Children.Count > 0;

    /// <summary>
    /// Number of additional archive entries (after the first) that resolved to this same
    /// relative path. Non-zero only when the archive contains duplicate entry paths; such
    /// duplicates are counted once in ancestor rollups (first-wins) so that a directory's
    /// size always equals the sum of its displayed children.
    /// </summary>
    public int DuplicateCount { get; set; }
}

public static class ArchiveHierarchy
{
    public static bool MatchPattern(string text, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        pattern = pattern.Trim();

        bool isNegated = pattern.StartsWith('!');
        if (isNegated)
        {
            pattern = pattern.Substring(1).Trim();
            if (string.IsNullOrEmpty(pattern)) return true;
        }

        var normalizedText = text.Replace('\\', '/').Trim('/');
        var normalizedPattern = pattern.Replace('\\', '/').Trim('/');

        bool matches;
        if (normalizedPattern.Contains('*') || normalizedPattern.Contains('?'))
        {
            if (!normalizedPattern.Contains('/'))
            {
                var fileName = Path.GetFileName(normalizedText);
                var fileNameRegex = "^" + Regex.Escape(normalizedPattern)
                    .Replace(@"\*", ".*")
                    .Replace(@"\?", ".") + "$";

                var fullPathRegex = "^" + Regex.Escape(normalizedPattern)
                    .Replace(@"\*", ".*")
                    .Replace(@"\?", ".") + "$";

                matches = Regex.IsMatch(fileName, fileNameRegex, RegexOptions.IgnoreCase)
                    || Regex.IsMatch(normalizedText, fullPathRegex, RegexOptions.IgnoreCase);
            }
            else
            {
                var escaped = Regex.Escape(normalizedPattern);
                escaped = escaped.Replace(@"/\*\*/", @"/(?:.*/)?");
                escaped = escaped.Replace(@"\*\*/", @"(?:.*/)?");
                escaped = escaped.Replace(@"/\*\*", @"(?:/.*)?");
                escaped = escaped.Replace(@"\*\*", @".*");
                escaped = escaped.Replace(@"\*", @"[^/]*");
                escaped = escaped.Replace(@"\?", @".");

                var regexPattern = "^" + escaped + "$";
                matches = Regex.IsMatch(normalizedText, regexPattern, RegexOptions.IgnoreCase);
            }
        }
        else
        {
            matches = normalizedText.Contains(normalizedPattern, StringComparison.OrdinalIgnoreCase);
        }

        return isNegated ? !matches : matches;
    }

    public static bool MatchesFilter(ArchiveEntry entry, string? filterText, Func<string, bool, bool>? categoryPredicate = null)
    {
        if (!entry.IsDirectory && categoryPredicate != null && !categoryPredicate(entry.RelativePath, entry.IsDirectory))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(filterText))
        {
            return true;
        }

        return MatchPattern(entry.RelativePath, filterText);
    }

    public static IReadOnlyList<ArchiveTreeNode> BuildTree(
        IEnumerable<ArchiveEntry> entries,
        string? filterText = null,
        Func<string, bool, bool>? categoryPredicate = null)
    {
        var allEntriesList = entries.ToList();
        var isFiltered = !string.IsNullOrWhiteSpace(filterText) || categoryPredicate != null;

        var filtered = isFiltered
            ? allEntriesList.Where(e => MatchesFilter(e, filterText, categoryPredicate)).ToList()
            : allEntriesList;

        var rootNodes = new List<ArchiveTreeNode>();
        var lookup = new Dictionary<string, ArchiveTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in filtered.OrderBy(e => e.RelativePath.Replace('\\', '/')))
        {
            var normalizedPath = entry.RelativePath.Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(normalizedPath)) continue;

            var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string currentPath = string.Empty;
            ArchiveTreeNode? parent = null;

            bool isDuplicatePath = lookup.ContainsKey(normalizedPath);

            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                currentPath = string.IsNullOrEmpty(currentPath) ? segment : $"{currentPath}/{segment}";
                bool isTarget = (i == segments.Length - 1);
                bool isLeaf = isTarget && !entry.IsDirectory;

                if (!lookup.TryGetValue(currentPath, out var node))
                {
                    node = new ArchiveTreeNode
                    {
                        Name = segment,
                        RelativePath = currentPath,
                        IsDirectory = !isLeaf,
                        UncompressedSize = isLeaf ? entry.UncompressedSize : 0,
                        CompressedSize = isLeaf ? entry.CompressedSize : 0,
                        LastModified = entry.LastModified,
                        Attributes = isTarget ? entry.Attributes : string.Empty
                    };

                    lookup[currentPath] = node;

                    if (parent == null)
                        rootNodes.Add(node);
                    else
                        parent.Children.Add(node);
                }
                else
                {
                    if (isLeaf)
                    {
                        node.DuplicateCount++;
                    }
                    else if (isTarget)
                    {
                        node.IsDirectory = true;
                        if (entry.LastModified.HasValue)
                        {
                            node.LastModified = entry.LastModified;
                        }
                        if (!string.IsNullOrEmpty(entry.Attributes))
                        {
                            node.Attributes = entry.Attributes;
                        }
                    }
                }

                if (!isLeaf && !isDuplicatePath && !entry.IsDirectory)
                {
                    node.UncompressedSize += entry.UncompressedSize;
                    if (entry.CompressedSize.HasValue)
                    {
                        node.CompressedSize = (node.CompressedSize ?? 0) + entry.CompressedSize.Value;
                    }
                }

                parent = node;
            }
        }

        return rootNodes;
    }
}

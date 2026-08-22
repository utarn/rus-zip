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
    public static IReadOnlyList<ArchiveTreeNode> BuildTree(
        IEnumerable<ArchiveEntry> entries,
        string? filterText = null)
    {
        var filtered = entries;
        if (!string.IsNullOrWhiteSpace(filterText))
        {
            filtered = entries.Where(e => e.RelativePath.Contains(filterText, StringComparison.OrdinalIgnoreCase));
        }

        var rootNodes = new List<ArchiveTreeNode>();
        var lookup = new Dictionary<string, ArchiveTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in filtered.OrderBy(e => e.RelativePath.Replace('\\', '/')))
        {
            var normalizedPath = entry.RelativePath.Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(normalizedPath)) continue;

            var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string currentPath = string.Empty;
            ArchiveTreeNode? parent = null;

            // F-20: when the same full path has already been materialized, this entry is a
            // duplicate. Its size must be counted once in ancestor rollups (first-wins) so
            // directory totals never exceed the sum of their displayed children.
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
                        // Duplicate leaf path: first-wins keeps the original entry's data
                        // (size and metadata) so the displayed tree stays self-consistent;
                        // surface the extra copy via DuplicateCount.
                        node.DuplicateCount++;
                    }
                    else if (isTarget)
                    {
                        // Existing node re-declared as a directory. Refresh directory metadata
                        // (last-wins for timestamps/attributes), but never re-add size.
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

                // Roll file sizes up to every ancestor directory, once per distinct path.
                // Directory entries carry metadata but no content size, so their declared size
                // is never added — a directory's displayed size is the sum of its children.
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

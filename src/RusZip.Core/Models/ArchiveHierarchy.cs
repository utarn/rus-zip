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
                        node.IsDirectory = false;
                        node.UncompressedSize = entry.UncompressedSize;
                        node.CompressedSize = entry.CompressedSize;
                        node.LastModified = entry.LastModified;
                        node.Attributes = entry.Attributes;
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

                if (!isLeaf && node != null)
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

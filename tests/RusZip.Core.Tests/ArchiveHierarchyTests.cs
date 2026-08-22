using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

public class ArchiveHierarchyTests
{
    [Fact]
    public void BuildTree_ConstructsHierarchicalTree_WithAggregatedSizes()
    {
        var entries = new List<ArchiveEntry>
        {
            new("docs", 0, 0, null, true),
            new("docs/readme.txt", 100, 50, null, false),
            new("docs/manual.pdf", 400, 200, null, false),
            new("images/logo.png", 500, 300, null, false),
            new("root.txt", 50, 25, null, false)
        };

        var roots = ArchiveHierarchy.BuildTree(entries);

        Assert.Equal(3, roots.Count); // docs, images, root.txt

        var docs = roots.FirstOrDefault(r => r.Name == "docs");
        Assert.NotNull(docs);
        Assert.True(docs.IsDirectory);
        Assert.True(docs.HasChildren);
        Assert.Equal("docs", docs.RelativePath);
        Assert.Equal(2, docs.Children.Count);
        Assert.Equal(500, docs.UncompressedSize); // 100 + 400
        Assert.Equal(250, docs.CompressedSize);   // 50 + 200

        var images = roots.FirstOrDefault(r => r.Name == "images");
        Assert.NotNull(images);
        Assert.True(images.IsDirectory);
        Assert.True(images.HasChildren);
        Assert.Single(images.Children);
        Assert.Equal(500, images.UncompressedSize);
        Assert.Equal(300, images.CompressedSize);

        var rootFile = roots.FirstOrDefault(r => r.Name == "root.txt");
        Assert.NotNull(rootFile);
        Assert.False(rootFile.IsDirectory);
        Assert.False(rootFile.HasChildren);
        Assert.Equal("root.txt", rootFile.RelativePath);
        Assert.Equal(50, rootFile.UncompressedSize);
        Assert.Equal(25, rootFile.CompressedSize);
    }

    [Fact]
    public void BuildTree_WithFilter_FiltersMatchingEntries()
    {
        var entries = new List<ArchiveEntry>
        {
            new("docs/readme.txt", 100, 50, null, false),
            new("docs/manual.pdf", 400, 200, null, false),
            new("images/logo.png", 500, 300, null, false)
        };

        var roots = ArchiveHierarchy.BuildTree(entries, "manual");

        Assert.Single(roots);
        var docs = roots[0];
        Assert.Equal("docs", docs.Name);
        Assert.Single(docs.Children);
        Assert.Equal("manual.pdf", docs.Children[0].Name);
        Assert.Equal(400, docs.UncompressedSize);
        Assert.Equal(200, docs.CompressedSize);
    }

    [Fact]
    public void BuildTree_EmptyEntries_ReturnsEmptyList()
    {
        var roots = ArchiveHierarchy.BuildTree([]);
        Assert.Empty(roots);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildTree_NullOrWhitespaceFilter_ReturnsAllEntries(string? filter)
    {
        var entries = new List<ArchiveEntry>
        {
            new("a.txt", 10, 5, null, false),
            new("b.txt", 20, 10, null, false)
        };

        var roots = ArchiveHierarchy.BuildTree(entries, filter);
        Assert.Equal(2, roots.Count);
    }

    [Fact]
    public void BuildTree_NormalizesBackslashesAndTrimsSlashes()
    {
        var entries = new List<ArchiveEntry>
        {
            new(@"\nested\deep\file.txt\", 100, 50, null, false)
        };

        var roots = ArchiveHierarchy.BuildTree(entries);

        Assert.Single(roots);
        var nested = roots[0];
        Assert.Equal("nested", nested.Name);
        Assert.Equal("nested", nested.RelativePath);
        Assert.Single(nested.Children);

        var deep = nested.Children[0];
        Assert.Equal("deep", deep.Name);
        Assert.Equal("nested/deep", deep.RelativePath);
        Assert.Single(deep.Children);

        var file = deep.Children[0];
        Assert.Equal("file.txt", file.Name);
        Assert.Equal("nested/deep/file.txt", file.RelativePath);
        Assert.False(file.IsDirectory);
    }

    [Fact]
    public void BuildTree_PreservesMetadataOnLeavesAndDirectories()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = new List<ArchiveEntry>
        {
            new("folder", 0, 0, now.AddHours(-1), true, false, "rwxr-xr-x"),
            new("folder/file.bin", 1024, 512, now, false, false, "rw-r--r--")
        };

        var roots = ArchiveHierarchy.BuildTree(entries);

        Assert.Single(roots);
        var folder = roots[0];
        Assert.True(folder.IsDirectory);
        Assert.Equal("rwxr-xr-x", folder.Attributes);
        Assert.Equal(now.AddHours(-1), folder.LastModified);

        var file = folder.Children[0];
        Assert.False(file.IsDirectory);
        Assert.Equal("rw-r--r--", file.Attributes);
        Assert.Equal(now, file.LastModified);
    }

    [Fact]
    public void BuildTree_HandlesNullCompressedSizes()
    {
        var entries = new List<ArchiveEntry>
        {
            new("folder/file1.txt", 100, null, null, false),
            new("folder/file2.txt", 200, 50, null, false)
        };

        var roots = ArchiveHierarchy.BuildTree(entries);

        Assert.Single(roots);
        var folder = roots[0];
        Assert.Equal(300, folder.UncompressedSize);
        Assert.Equal(50, folder.CompressedSize);

        var file1 = folder.Children.First(c => c.Name == "file1.txt");
        Assert.Null(file1.CompressedSize);

        var file2 = folder.Children.First(c => c.Name == "file2.txt");
        Assert.Equal(50, file2.CompressedSize);
    }

    [Fact]
    public void BuildTree_DeeplyNestedTree_AggregatesSizesUpToRoot()
    {
        var entries = new List<ArchiveEntry>
        {
            new("a/b/c/d/e/leaf.dat", 1000, 400, null, false)
        };

        var roots = ArchiveHierarchy.BuildTree(entries);

        Assert.Single(roots);
        var a = roots[0];
        Assert.Equal(1000, a.UncompressedSize);
        Assert.Equal(400, a.CompressedSize);

        var b = a.Children[0];
        Assert.Equal(1000, b.UncompressedSize);
        Assert.Equal(400, b.CompressedSize);

        var c = b.Children[0];
        Assert.Equal(1000, c.UncompressedSize);
        Assert.Equal(400, c.CompressedSize);

        var d = c.Children[0];
        Assert.Equal(1000, d.UncompressedSize);
        Assert.Equal(400, d.CompressedSize);

        var e = d.Children[0];
        Assert.Equal(1000, e.UncompressedSize);
        Assert.Equal(400, e.CompressedSize);

        var leaf = e.Children[0];
        Assert.Equal(1000, leaf.UncompressedSize);
        Assert.Equal(400, leaf.CompressedSize);
        Assert.False(leaf.IsDirectory);
    }

    [Fact]
    public void BuildTree_DuplicateLeafPaths_CountedOnceInRollups()
    {
        var entries = new List<ArchiveEntry>
        {
            new("docs/manual.pdf", 400, 200, null, false),
            new("docs/readme.txt", 100, 50, null, false),
            new("docs/readme.txt", 500, 250, null, false) // duplicate path, larger size
        };

        var roots = ArchiveHierarchy.BuildTree(entries);

        Assert.Single(roots);
        var docs = roots[0];
        Assert.True(docs.IsDirectory);

        var manual = docs.Children.Single(c => c.Name == "manual.pdf");
        var readme = docs.Children.Single(c => c.Name == "readme.txt");

        // First-wins dedupe: the leaf keeps the first occurrence's data.
        Assert.Equal(400, manual.UncompressedSize);
        Assert.Equal(200, manual.CompressedSize);
        Assert.Equal(0, manual.DuplicateCount);

        Assert.Equal(100, readme.UncompressedSize);
        Assert.Equal(50, readme.CompressedSize);
        Assert.Equal(1, readme.DuplicateCount);

        // Directory rollup counts each distinct path exactly once: 400 + 100, not 400 + 100 + 500.
        Assert.Equal(500, docs.UncompressedSize);
        Assert.Equal(250, docs.CompressedSize);

        // Rollup invariant: a directory's size == sum of its displayed children.
        Assert.Equal(manual.UncompressedSize + readme.UncompressedSize, docs.UncompressedSize);
    }

    [Fact]
    public void BuildTree_DuplicateDirectoryEntries_DoNotDoubleCountSize()
    {
        var entries = new List<ArchiveEntry>
        {
            new("folder", 0, 0, null, true),
            new("folder", 0, 0, null, true), // duplicate directory entry
            new("folder/file.txt", 250, 100, null, false)
        };

        var roots = ArchiveHierarchy.BuildTree(entries);

        Assert.Single(roots);
        var folder = roots[0];
        Assert.True(folder.IsDirectory);
        Assert.Equal(250, folder.UncompressedSize);
        Assert.Equal(100, folder.CompressedSize);
        Assert.Equal(250, folder.Children.Sum(c => c.UncompressedSize));
    }

    [Fact]
    public void BuildTree_RollupInvariant_EveryDirectorySizeEqualsSumOfDisplayedChildren()
    {
        var entries = new List<ArchiveEntry>
        {
            new("src", 0, 0, null, true),
            new("src/Models", 0, 0, null, true),
            new("src/Models/User.cs", 200, 80, null, false),
            new("src/Models/Order.cs", 300, 120, null, false),
            new("src/Controllers", 0, 0, null, true),
            new("src/Controllers/HomeController.cs", 500, 200, null, false),
            new("src/Program.cs", 100, 40, null, false),
            new("README.md", 50, 20, null, false)
        };

        var roots = ArchiveHierarchy.BuildTree(entries);

        AssertRollupInvariant(roots);
    }

    private static void AssertRollupInvariant(IEnumerable<ArchiveTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsDirectory)
            {
                Assert.Equal(
                    node.Children.Sum(c => c.UncompressedSize),
                    node.UncompressedSize);
                Assert.Equal(
                    node.Children.Sum(c => c.CompressedSize ?? 0),
                    node.CompressedSize ?? 0);
            }
            AssertRollupInvariant(node.Children);
        }
    }
}

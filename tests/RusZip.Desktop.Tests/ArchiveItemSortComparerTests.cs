using System.ComponentModel;
using RusZip.Core.Models;
using RusZip.Desktop.ViewModels;
using Xunit;

namespace RusZip.Desktop.Tests;

public class ArchiveItemSortComparerTests
{
    [Fact]
    public void ArchiveItemSortComparer_NullAndReferenceEquality()
    {
        var comparer = ArchiveItemComparer.CreateName();

        var node = new ArchiveTreeNode
        {
            Name = "file.txt",
            RelativePath = "file.txt",
            IsDirectory = false
        };
        var item = ArchiveItemViewModel.FromTreeNode(node, false);

        Assert.Equal(0, comparer.Compare(item, item));
        Assert.Equal(0, comparer.Compare(null, null));
        Assert.Equal(-1, comparer.Compare(null, item));
        Assert.Equal(1, comparer.Compare(item, null));
        Assert.Equal(-1, comparer.Compare("not-an-item", item));
    }

    [Fact]
    public void ArchiveItemSortComparer_DirectoriesAlwaysFirst_InBothSortDirections()
    {
        var dirNode = new ArchiveTreeNode
        {
            Name = "FolderA",
            RelativePath = "FolderA",
            IsDirectory = true
        };
        var fileNode = new ArchiveTreeNode
        {
            Name = "FileB.txt",
            RelativePath = "FileB.txt",
            IsDirectory = false
        };

        var dirItem = ArchiveItemViewModel.FromTreeNode(dirNode, false);
        var fileItem = ArchiveItemViewModel.FromTreeNode(fileNode, false);

        var nameComparer = ArchiveItemComparer.CreateName();

        // Ascending: dir sorts before file (-1)
        nameComparer.Direction = ListSortDirection.Ascending;
        Assert.True(nameComparer.Compare(dirItem, fileItem) < 0);
        Assert.True(nameComparer.Compare(fileItem, dirItem) > 0);

        // Descending: dir inverted to (1) so that the grid's negation restores directories-first
        nameComparer.Direction = ListSortDirection.Descending;
        Assert.True(nameComparer.Compare(dirItem, fileItem) > 0);
        Assert.True(nameComparer.Compare(fileItem, dirItem) < 0);
    }

    [Fact]
    public void ArchiveItemSortComparer_ComparesAllPropertiesCorrectly()
    {
        var node1 = new ArchiveTreeNode
        {
            Name = "Alpha.txt",
            RelativePath = "Alpha.txt",
            IsDirectory = false,
            UncompressedSize = 100,
            CompressedSize = 50,
            LastModified = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Attributes = "Archive"
        };
        var node2 = new ArchiveTreeNode
        {
            Name = "Beta.txt",
            RelativePath = "Beta.txt",
            IsDirectory = false,
            UncompressedSize = 200,
            CompressedSize = 100,
            LastModified = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            Attributes = "ReadOnly"
        };

        var item1 = ArchiveItemViewModel.FromTreeNode(node1, false);
        var item2 = ArchiveItemViewModel.FromTreeNode(node2, false);

        // Name
        var nameComp = ArchiveItemComparer.CreateName();
        Assert.True(nameComp.Compare(item1, item2) < 0);

        // Size
        var sizeComp = ArchiveItemComparer.CreateSize();
        Assert.True(sizeComp.Compare(item1, item2) < 0);

        // Compressed
        var compComp = ArchiveItemComparer.CreateCompressed();
        Assert.True(compComp.Compare(item1, item2) < 0);

        // Modified
        var modComp = ArchiveItemComparer.CreateModified();
        Assert.True(modComp.Compare(item1, item2) < 0);

        // Ratio
        var ratioComp = ArchiveItemComparer.CreateRatio();
        Assert.Equal(0, ratioComp.Compare(item1, item2));

        // Attributes
        var attrComp = ArchiveItemComparer.CreateAttributes();
        Assert.True(attrComp.Compare(item1, item2) < 0);
    }
}

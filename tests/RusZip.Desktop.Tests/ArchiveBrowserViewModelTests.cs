using System.ComponentModel;
using Avalonia.Controls;
using RusZip.Core.Models;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Tests;

public class ArchiveBrowserViewModelTests
{
    [Fact]
    public void LoadEntries_ConstructsHierarchicalTree_AndComputesTotals()
    {
        var browser = new ArchiveBrowserViewModel();
        var now = DateTimeOffset.UtcNow;

        var entries = new List<ArchiveEntry>
        {
            new("folder/subfolder/file1.txt", 1000, 500, now, false, false, "rw-r--r--"),
            new("folder/subfolder/file2.txt", 2000, 800, now, false, false, "rw-r--r--"),
            new("folder/root_file.txt", 500, 200, now, false, false, "rw-r--r--"),
            new("top_level.txt", 300, 100, now, false, false, "rw-r--r--")
        };

        browser.LoadEntries("/path/to/archive.zrus", entries);

        Assert.Equal("/path/to/archive.zrus", browser.LoadedArchivePath);
        Assert.Equal(4, browser.TotalEntries);
        Assert.Equal(3800, browser.TotalUncompressedBytes);
        Assert.Equal(1600, browser.TotalCompressedBytes);
        Assert.Equal("3.71 KB", browser.FormattedTotalUncompressedSize);
        Assert.Equal("1.56 KB", browser.FormattedTotalCompressedSize);
        Assert.Equal("42.1%", browser.FormattedTotalRatio);

        Assert.Equal(2, browser.RootItems.Count);

        var folderNode = browser.RootItems.FirstOrDefault(x => x.Name == "folder");
        Assert.NotNull(folderNode);
        Assert.True(folderNode.IsDirectory);
        Assert.Equal(3500, folderNode.UncompressedSize);
        Assert.Equal(1500, folderNode.CompressedSize);
        Assert.Equal(2, folderNode.Children.Count);

        var subfolderNode = folderNode.Children.FirstOrDefault(x => x.Name == "subfolder");
        Assert.NotNull(subfolderNode);
        Assert.True(subfolderNode.IsDirectory);
        Assert.Equal(3000, subfolderNode.UncompressedSize);
        Assert.Equal(1300, subfolderNode.CompressedSize);
        Assert.Equal(2, subfolderNode.Children.Count);

        var file1 = subfolderNode.Children.FirstOrDefault(x => x.Name == "file1.txt");
        Assert.NotNull(file1);
        Assert.False(file1.IsDirectory);
        Assert.Equal(1000, file1.UncompressedSize);
        Assert.Equal(500, file1.CompressedSize);

        var topLevelFile = browser.RootItems.FirstOrDefault(x => x.Name == "top_level.txt");
        Assert.NotNull(topLevelFile);
        Assert.False(topLevelFile.IsDirectory);
        Assert.Equal(300, topLevelFile.UncompressedSize);
    }

    [Fact]
    public void LoadEntries_NormalizesBackslashesInPaths()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new(@"nested\deep\file.txt", 100, 50, DateTimeOffset.UtcNow, false)
        };

        browser.LoadEntries("test.zip", entries);

        Assert.Single(browser.RootItems);
        var root = browser.RootItems[0];
        Assert.Equal("nested", root.Name);
        Assert.Single(root.Children);
        Assert.Equal("deep", root.Children[0].Name);
        Assert.Single(root.Children[0].Children);
        Assert.Equal("file.txt", root.Children[0].Children[0].Name);
    }

    [Fact]
    public void LoadEntries_ConfiguresGridSource_WithAllRequiredColumns()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("file.txt", 100, 50, DateTimeOffset.UtcNow, false, false, "rw-r--r--")
        };

        browser.LoadEntries("test.zip", entries);

        Assert.NotNull(browser.GridSource);
        Assert.Equal(6, browser.GridSource.Columns.Count);

        Assert.Equal("Name", browser.GridSource.Columns[0].Header?.ToString());
        Assert.Equal("Size", browser.GridSource.Columns[1].Header?.ToString());
        Assert.Equal("Compressed", browser.GridSource.Columns[2].Header?.ToString());
        Assert.Equal("Ratio", browser.GridSource.Columns[3].Header?.ToString());
        Assert.Equal("Modified", browser.GridSource.Columns[4].Header?.ToString());
        Assert.Equal("Attributes", browser.GridSource.Columns[5].Header?.ToString());

        foreach (var column in browser.GridSource.Columns)
        {
            Assert.True(column.CanUserResize);
            if (column is Avalonia.Controls.Models.TreeDataGrid.ColumnBase<ArchiveItemViewModel> colBase)
            {
                Assert.True(colBase.Options?.CanUserSortColumn);
                Assert.True(colBase.Options?.CanUserResizeColumn);
            }
        }
    }

    [Fact]
    public void Sorting_ByNameColumn_KeepsDirectoriesFirst_AndSortsAlphabetically()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("z_file.txt", 100, 50, null, false),
            new("a_file.txt", 200, 100, null, false),
            new("b_folder/item.txt", 300, 150, null, false)
        };

        browser.LoadEntries("test.zip", entries);
        var source = browser.GridSource!;

        source.SortBy(source.Columns[0], ListSortDirection.Ascending);
        var item0 = GetRowModel(source, 0);
        var item1 = GetRowModel(source, 1);
        var item2 = GetRowModel(source, 2);

        Assert.Equal("b_folder", item0.Name);
        Assert.Equal("a_file.txt", item1.Name);
        Assert.Equal("z_file.txt", item2.Name);

        source.SortBy(source.Columns[0], ListSortDirection.Descending);
        var desc0 = GetRowModel(source, 0);
        var desc1 = GetRowModel(source, 1);
        var desc2 = GetRowModel(source, 2);

        Assert.Equal("b_folder", desc0.Name);
        Assert.Equal("z_file.txt", desc1.Name);
        Assert.Equal("a_file.txt", desc2.Name);
    }

    [Fact]
    public void Sorting_BySizeColumn_SortsNumerically()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("medium.txt", 500, 250, null, false),
            new("large.txt", 1000, 500, null, false),
            new("small.txt", 100, 50, null, false)
        };

        browser.LoadEntries("test.zip", entries);
        var source = browser.GridSource!;

        source.SortBy(source.Columns[1], ListSortDirection.Ascending);
        Assert.Equal("small.txt", GetRowModel(source, 0).Name);
        Assert.Equal("medium.txt", GetRowModel(source, 1).Name);
        Assert.Equal("large.txt", GetRowModel(source, 2).Name);

        source.SortBy(source.Columns[1], ListSortDirection.Descending);
        Assert.Equal("large.txt", GetRowModel(source, 0).Name);
        Assert.Equal("medium.txt", GetRowModel(source, 1).Name);
        Assert.Equal("small.txt", GetRowModel(source, 2).Name);
    }

    [Fact]
    public void Sorting_ByCompressedColumn_SortsNumerically()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("fileB.txt", 1000, 300, null, false),
            new("fileA.txt", 1000, 100, null, false),
            new("fileC.txt", 1000, 600, null, false)
        };

        browser.LoadEntries("test.zip", entries);
        var source = browser.GridSource!;

        source.SortBy(source.Columns[2], ListSortDirection.Ascending);
        Assert.Equal("fileA.txt", GetRowModel(source, 0).Name);
        Assert.Equal("fileB.txt", GetRowModel(source, 1).Name);
        Assert.Equal("fileC.txt", GetRowModel(source, 2).Name);
    }

    [Fact]
    public void Sorting_ByModifiedColumn_SortsChronologically()
    {
        var browser = new ArchiveBrowserViewModel();
        var date1 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var date2 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var date3 = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        var entries = new List<ArchiveEntry>
        {
            new("file2.txt", 100, 50, date2, false),
            new("file1.txt", 100, 50, date1, false),
            new("file3.txt", 100, 50, date3, false)
        };

        browser.LoadEntries("test.zip", entries);
        var source = browser.GridSource!;

        source.SortBy(source.Columns[4], ListSortDirection.Ascending);
        Assert.Equal("file1.txt", GetRowModel(source, 0).Name);
        Assert.Equal("file2.txt", GetRowModel(source, 1).Name);
        Assert.Equal("file3.txt", GetRowModel(source, 2).Name);
    }

    private static ArchiveItemViewModel GetRowModel(HierarchicalTreeDataGridSource<ArchiveItemViewModel> source, int index)
    {
        return (ArchiveItemViewModel)source.Rows[index].Model!;
    }

    [Fact]
    public void FilterText_FiltersEntries_AndAutoExpandsTree()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("documents/reports/q1.pdf", 1000, 500, null, false),
            new("documents/reports/q2.pdf", 1000, 500, null, false),
            new("images/photo.png", 2000, 1800, null, false)
        };

        browser.LoadEntries("archive.zrus", entries);
        Assert.Equal(2, browser.RootItems.Count);

        browser.FilterText = "q1";
        Assert.Single(browser.RootItems);
        var docFolder = browser.RootItems[0];
        Assert.Equal("documents", docFolder.Name);
        Assert.True(docFolder.IsExpanded);
        Assert.Single(docFolder.Children);
        var reportsFolder = docFolder.Children[0];
        Assert.Equal("reports", reportsFolder.Name);
        Assert.True(reportsFolder.IsExpanded);
        Assert.Single(reportsFolder.Children);
        Assert.Equal("q1.pdf", reportsFolder.Children[0].Name);

        browser.ClearFilter();
        Assert.Equal(string.Empty, browser.FilterText);
        Assert.Equal(2, browser.RootItems.Count);
    }

    [Fact]
    public void ExpandAllAndCollapseAll_WorksRecursively()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("dir1/dir2/dir3/file.txt", 100, 50, null, false)
        };

        browser.LoadEntries("test.zip", entries);

        browser.ExpandAll();
        var dir1 = browser.RootItems[0];
        var dir2 = dir1.Children[0];
        var dir3 = dir2.Children[0];
        Assert.True(dir1.IsExpanded);
        Assert.True(dir2.IsExpanded);
        Assert.True(dir3.IsExpanded);

        browser.CollapseAll();
        Assert.False(dir1.IsExpanded);
        Assert.False(dir2.IsExpanded);
        Assert.False(dir3.IsExpanded);
    }

    [Fact]
    public async Task RequestExtractAsync_InvokesExtractRequestedEvent()
    {
        var browser = new ArchiveBrowserViewModel();
        bool wasCalled = false;
        browser.ExtractRequested += () =>
        {
            wasCalled = true;
            return Task.CompletedTask;
        };

        await browser.RequestExtractAsync();
        Assert.True(wasCalled);
    }
}

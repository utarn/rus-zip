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
        Assert.Equal("3.7 KB", browser.FormattedTotalUncompressedSize);
        Assert.Equal("1.6 KB", browser.FormattedTotalCompressedSize);
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

    [Fact]
    public void Breadcrumbs_InitialState_ContainsSingleRootSegment()
    {
        var browser = new ArchiveBrowserViewModel();
        Assert.Single(browser.Breadcrumbs);
        var root = browser.Breadcrumbs[0];
        Assert.Equal("Archive", root.Name);
        Assert.Equal(string.Empty, root.FullPath);
        Assert.True(root.IsRoot);
        Assert.True(root.IsLast);
        Assert.Equal(Avalonia.Media.FontWeight.SemiBold, root.FontWeight);
        Assert.NotNull(root.NavigateCommand);
    }

    [Fact]
    public void Breadcrumbs_OnItemSelection_GeneratesHierarchicalSegments()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("src/RusZip.Desktop/App.axaml", 100, 50, null, false)
        };
        browser.LoadEntries("test.zip", entries);

        // Initially at root
        Assert.Single(browser.Breadcrumbs);
        Assert.Equal("Archive", browser.Breadcrumbs[0].Name);

        // Select nested file
        var fileItem = browser.FindItemByPath("src/RusZip.Desktop/App.axaml");
        Assert.NotNull(fileItem);
        browser.SelectedItem = fileItem;

        Assert.Equal(4, browser.Breadcrumbs.Count);

        Assert.Equal("Archive", browser.Breadcrumbs[0].Name);
        Assert.Equal(string.Empty, browser.Breadcrumbs[0].FullPath);
        Assert.True(browser.Breadcrumbs[0].IsRoot);
        Assert.False(browser.Breadcrumbs[0].IsLast);
        Assert.Equal(Avalonia.Media.FontWeight.Normal, browser.Breadcrumbs[0].FontWeight);

        Assert.Equal("src", browser.Breadcrumbs[1].Name);
        Assert.Equal("src", browser.Breadcrumbs[1].FullPath);
        Assert.False(browser.Breadcrumbs[1].IsRoot);
        Assert.False(browser.Breadcrumbs[1].IsLast);
        Assert.Equal(Avalonia.Media.FontWeight.Normal, browser.Breadcrumbs[1].FontWeight);

        Assert.Equal("RusZip.Desktop", browser.Breadcrumbs[2].Name);
        Assert.Equal("src/RusZip.Desktop", browser.Breadcrumbs[2].FullPath);
        Assert.False(browser.Breadcrumbs[2].IsRoot);
        Assert.False(browser.Breadcrumbs[2].IsLast);
        Assert.Equal(Avalonia.Media.FontWeight.Normal, browser.Breadcrumbs[2].FontWeight);

        Assert.Equal("App.axaml", browser.Breadcrumbs[3].Name);
        Assert.Equal("src/RusZip.Desktop/App.axaml", browser.Breadcrumbs[3].FullPath);
        Assert.False(browser.Breadcrumbs[3].IsRoot);
        Assert.True(browser.Breadcrumbs[3].IsLast);
        Assert.Equal(Avalonia.Media.FontWeight.SemiBold, browser.Breadcrumbs[3].FontWeight);
    }

    [Fact]
    public void BreadcrumbNavigation_ToParentSegment_SelectsFolderAndUpdatesBreadcrumbs()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("src/RusZip.Desktop/App.axaml", 100, 50, null, false)
        };
        browser.LoadEntries("test.zip", entries);

        var fileItem = browser.FindItemByPath("src/RusZip.Desktop/App.axaml");
        browser.SelectedItem = fileItem;
        Assert.Equal(4, browser.Breadcrumbs.Count);

        // Click intermediate breadcrumb "src"
        var srcBreadcrumb = browser.Breadcrumbs[1];
        browser.NavigateToBreadcrumbCommand.Execute(srcBreadcrumb);

        Assert.NotNull(browser.SelectedItem);
        Assert.Equal("src", browser.SelectedItem.Name);
        Assert.True(browser.SelectedItem.IsDirectory);

        // Breadcrumbs now contain Archive > src
        Assert.Equal(2, browser.Breadcrumbs.Count);
        Assert.Equal("Archive", browser.Breadcrumbs[0].Name);
        Assert.False(browser.Breadcrumbs[0].IsLast);
        Assert.Equal("src", browser.Breadcrumbs[1].Name);
        Assert.True(browser.Breadcrumbs[1].IsLast);
    }

    [Fact]
    public void BreadcrumbNavigation_ToRoot_ClearsSelectionAndResetsBreadcrumbs()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("src/RusZip.Desktop/App.axaml", 100, 50, null, false)
        };
        browser.LoadEntries("test.zip", entries);

        var fileItem = browser.FindItemByPath("src/RusZip.Desktop/App.axaml");
        browser.SelectedItem = fileItem;

        // Navigate to root via string ""
        browser.NavigateToBreadcrumbCommand.Execute(string.Empty);
        Assert.Null(browser.SelectedItem);
        Assert.Single(browser.Breadcrumbs);
        Assert.Equal("Archive", browser.Breadcrumbs[0].Name);
        Assert.True(browser.Breadcrumbs[0].IsLast);

        // Select again and navigate to root via "(root)"
        browser.SelectedItem = fileItem;
        browser.NavigateToBreadcrumbCommand.Execute("(root)");
        Assert.Null(browser.SelectedItem);
        Assert.Single(browser.Breadcrumbs);

        // Select again and navigate to root via root breadcrumb object
        browser.SelectedItem = fileItem;
        browser.NavigateToBreadcrumbCommand.Execute(browser.Breadcrumbs[0]);
        Assert.Null(browser.SelectedItem);
        Assert.Single(browser.Breadcrumbs);
    }

    [Fact]
    public void BreadcrumbNavigation_ToDeepPath_AutoExpandsAncestors()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("a/b/c/file.txt", 10, 5, null, false)
        };
        browser.LoadEntries("test.zip", entries);

        var a = browser.FindItemByPath("a")!;
        var b = browser.FindItemByPath("a/b")!;
        var c = browser.FindItemByPath("a/b/c")!;

        Assert.False(a.IsExpanded);
        Assert.False(b.IsExpanded);
        Assert.False(c.IsExpanded);

        browser.NavigateToPath("a/b/c");

        Assert.True(a.IsExpanded);
        Assert.True(b.IsExpanded);
        Assert.True(c.IsExpanded);
        Assert.NotNull(browser.SelectedItem);
        Assert.Equal("c", browser.SelectedItem.Name);
    }

    [Fact]
    public async Task CopyPathCommand_WithSelectedItem_CopiesPathAndInvokesEvents()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("folder/subfolder/file.txt", 100, 50, null, false)
        };
        browser.LoadEntries("test.zip", entries);

        string? copiedText = null;
        browser.CopyToClipboardService = text =>
        {
            copiedText = text;
            return Task.CompletedTask;
        };

        string? eventPath = null;
        browser.CopyPathRequested += path =>
        {
            eventPath = path;
            return Task.CompletedTask;
        };

        var item = browser.FindItemByPath("folder/subfolder/file.txt");
        browser.SelectedItem = item;

        await browser.CopyPathCommand.ExecuteAsync(null);

        Assert.Equal("folder/subfolder/file.txt", copiedText);
        Assert.Equal("folder/subfolder/file.txt", eventPath);
    }

    [Fact]
    public async Task CopyPathCommand_WithExplicitItemParameter_CopiesParameterPath()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("file1.txt", 100, 50, null, false),
            new("file2.txt", 100, 50, null, false)
        };
        browser.LoadEntries("test.zip", entries);

        string? copiedText = null;
        browser.CopyToClipboardService = text =>
        {
            copiedText = text;
            return Task.CompletedTask;
        };

        var item1 = browser.FindItemByPath("file1.txt");
        var item2 = browser.FindItemByPath("file2.txt");
        browser.SelectedItem = item1;

        // Copy item2 explicitly despite item1 being selected
        await browser.CopyPathCommand.ExecuteAsync(item2);
        Assert.Equal("file2.txt", copiedText);

        // Copy using path string
        await browser.CopyPathCommand.ExecuteAsync("file1.txt");
        Assert.Equal("file1.txt", copiedText);
    }

    [Fact]
    public async Task CopyPathCommand_WhenNothingSelected_DoesNothingGracefully()
    {
        var browser = new ArchiveBrowserViewModel();
        string? copiedText = null;
        browser.CopyToClipboardService = text =>
        {
            copiedText = text;
            return Task.CompletedTask;
        };

        await browser.CopyPathCommand.ExecuteAsync(null);
        Assert.Null(copiedText);
    }

    [Fact]
    public async Task CopySelectedItemPathCommand_ExecutesCopyPathForSelectedItem()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("docs/manual.pdf", 100, 50, null, false)
        };
        browser.LoadEntries("test.zip", entries);

        string? copiedText = null;
        browser.CopyToClipboardService = text =>
        {
            copiedText = text;
            return Task.CompletedTask;
        };

        browser.SelectedItem = browser.FindItemByPath("docs/manual.pdf");
        await browser.CopySelectedItemPathCommand.ExecuteAsync(null);
        Assert.Equal("docs/manual.pdf", copiedText);
    }

    [Fact]
    public async Task ExtractSelectedItemCommand_WhenItemSelected_InvokesExtractItemRequested()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("nested/entry.txt", 100, 50, null, false)
        };
        browser.LoadEntries("test.zip", entries);

        ArchiveItemViewModel? extractedItem = null;
        browser.ExtractItemRequested += item =>
        {
            extractedItem = item;
            return Task.CompletedTask;
        };

        var target = browser.FindItemByPath("nested/entry.txt");
        browser.SelectedItem = target;

        await browser.ExtractSelectedItemCommand.ExecuteAsync(null);

        Assert.NotNull(extractedItem);
        Assert.Same(target, extractedItem);
    }

    [Fact]
    public async Task ExtractSelectedItemCommand_WhenNoItemSelected_InvokesExtractRequested()
    {
        var browser = new ArchiveBrowserViewModel();
        bool extractAllCalled = false;
        browser.ExtractRequested += () =>
        {
            extractAllCalled = true;
            return Task.CompletedTask;
        };

        browser.SelectedItem = null;
        await browser.ExtractSelectedItemCommand.ExecuteAsync(null);
        Assert.True(extractAllCalled);
    }

    [Fact]
    public async Task ExtractItemCommand_WithExplicitParameter_InvokesExtractItemRequested()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("fileA.txt", 100, 50, null, false),
            new("fileB.txt", 100, 50, null, false)
        };
        browser.LoadEntries("test.zip", entries);

        ArchiveItemViewModel? extractedItem = null;
        browser.ExtractItemRequested += item =>
        {
            extractedItem = item;
            return Task.CompletedTask;
        };

        var itemB = browser.FindItemByPath("fileB.txt");
        await browser.ExtractItemCommand.ExecuteAsync(itemB);

        Assert.NotNull(extractedItem);
        Assert.Equal("fileB.txt", extractedItem.Name);
    }

    [Fact]
    public void BreadcrumbItemViewModel_ConstructorsAndProperties_NotifyCorrectly()
    {
        var item = new BreadcrumbItemViewModel("test", "folder/test", isLast: false, isRoot: false);
        Assert.Equal("test", item.Name);
        Assert.Equal("folder/test", item.FullPath);
        Assert.False(item.IsLast);
        Assert.False(item.IsRoot);
        Assert.Equal(Avalonia.Media.FontWeight.Normal, item.FontWeight);

        var changed = new List<string>();
        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) changed.Add(e.PropertyName);
        };

        item.IsLast = true;
        Assert.True(item.IsLast);
        Assert.Equal(Avalonia.Media.FontWeight.SemiBold, item.FontWeight);
        Assert.Contains(nameof(BreadcrumbItemViewModel.IsLast), changed);
        Assert.Contains(nameof(BreadcrumbItemViewModel.FontWeight), changed);
    }
}

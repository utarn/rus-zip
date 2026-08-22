using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Headless.XUnit;
using RusZip.Core.Models;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;

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
    public void LoadEntries_ConfiguresHierarchicalGridSource_AndColumnSortComparers()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("file.txt", 100, 50, DateTimeOffset.UtcNow, false, false, "rw-r--r--")
        };

        browser.LoadEntries("test.zip", entries);

        // GridSource is now the ProDataGrid hierarchical model that drives the DataGrid rows.
        Assert.NotNull(browser.GridSource);
        Assert.True(browser.GridSource.IsVirtualRoot);
        Assert.Single(browser.GridSource!.RootItems!.Cast<object>());

        // The 6-column layout lives in ArchiveBrowserView.axaml; the ViewModel exposes the
        // same sort comparers the view's code-behind assigns to the DataGrid columns.
        Assert.NotNull(browser.NameColumnSortComparer);
        Assert.NotNull(browser.SizeColumnSortComparer);
        Assert.NotNull(browser.CompressedColumnSortComparer);
        Assert.NotNull(browser.RatioColumnSortComparer);
        Assert.NotNull(browser.ModifiedColumnSortComparer);
        Assert.NotNull(browser.AttributesColumnSortComparer);
    }

    [AvaloniaFact]
    public void ArchiveBrowserView_ConfiguresSixDataGridColumns_WithViewModelSortComparers()
    {
        var browser = new ArchiveBrowserViewModel();
        browser.LoadEntries("test.zip", new List<ArchiveEntry>
        {
            new("file.txt", 100, 50, DateTimeOffset.UtcNow, false)
        });

        var view = new ArchiveBrowserView { DataContext = browser };
        var grid = view.FindControl<DataGrid>("ArchiveGrid");

        Assert.NotNull(grid);
        Assert.Equal(6, grid.Columns.Count);
        Assert.Equal("Name", grid.Columns[0].Header?.ToString());
        Assert.Equal("Size", grid.Columns[1].Header?.ToString());
        Assert.Equal("Compressed", grid.Columns[2].Header?.ToString());
        Assert.Equal("Ratio", grid.Columns[3].Header?.ToString());
        Assert.Equal("Modified", grid.Columns[4].Header?.ToString());
        Assert.Equal("Attributes", grid.Columns[5].Header?.ToString());

        // The hierarchy column is a DataGridHierarchicalColumn (declarative expander/indent).
        Assert.IsType<DataGridHierarchicalColumn>(grid.Columns[0]);

        // The view code-behind assigns the ViewModel's comparers to the CLR CustomSortComparer
        // properties (they cannot be bound from XAML), including the numeric Ratio comparer.
        Assert.Same(browser.NameColumnSortComparer, grid.Columns[0].CustomSortComparer);
        Assert.Same(browser.SizeColumnSortComparer, grid.Columns[1].CustomSortComparer);
        Assert.Same(browser.CompressedColumnSortComparer, grid.Columns[2].CustomSortComparer);
        Assert.Same(browser.RatioColumnSortComparer, grid.Columns[3].CustomSortComparer);
        Assert.Same(browser.ModifiedColumnSortComparer, grid.Columns[4].CustomSortComparer);
        Assert.Same(browser.AttributesColumnSortComparer, grid.Columns[5].CustomSortComparer);
    }

    [AvaloniaFact]
    public void ArchiveBrowserView_ColumnSortDirectionChange_UpdatesComparerDirection()
    {
        var browser = new ArchiveBrowserViewModel();
        browser.LoadEntries("test.zip", new List<ArchiveEntry>
        {
            new("file.txt", 100, 50, DateTimeOffset.UtcNow, false)
        });

        var view = new ArchiveBrowserView { DataContext = browser };
        var grid = view.FindControl<DataGrid>("ArchiveGrid");
        Assert.NotNull(grid);
        var nameColumn = grid.Columns[0];

        // The grid syncs each column's SortDirection before applying the sort; the view
        // forwards those changes into the direction-aware comparers.
        nameColumn.SetCurrentValue(DataGridColumn.SortDirectionProperty, ListSortDirection.Descending);
        Assert.Equal(ListSortDirection.Descending, browser.NameColumnSortComparer.Direction);

        nameColumn.SetCurrentValue(DataGridColumn.SortDirectionProperty, ListSortDirection.Ascending);
        Assert.Equal(ListSortDirection.Ascending, browser.NameColumnSortComparer.Direction);

        // Clearing the sort (null direction) resets the comparers to ascending.
        nameColumn.SetCurrentValue(DataGridColumn.SortDirectionProperty, null);
        Assert.Equal(ListSortDirection.Ascending, browser.NameColumnSortComparer.Direction);
    }

    [AvaloniaFact]
    public void ContextMenu_RightClickedRowIsPassedAsCommandParameterToEveryItem()
    {
        var browser = new ArchiveBrowserViewModel();
        browser.LoadEntries("test.zip", new List<ArchiveEntry>
        {
            new("fileA.txt", 100, 50, null, false),
            new("fileB.txt", 200, 100, null, false)
        });

        var view = new ArchiveBrowserView { DataContext = browser };
        var grid = view.FindControl<DataGrid>("ArchiveGrid");
        var menu = grid!.ContextMenu;
        Assert.NotNull(menu);

        var menuItems = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(2, menuItems.Count);

        // Simulate a right-click on row B: every item receives B as CommandParameter (F-40).
        var itemB = browser.FindItemByPath("fileB.txt")!;
        view.SetContextMenuTarget(itemB);

        Assert.Same(itemB, menuItems[0].CommandParameter);
        Assert.Same(itemB, menuItems[1].CommandParameter);

        // Keyboard invocation (no pointer target) falls back to the selected row.
        browser.SelectedItem = browser.FindItemByPath("fileA.txt");
        view.SetContextMenuTarget(null);
        view.ApplyContextMenuFallbackToSelection();
        Assert.Same(browser.SelectedItem, menuItems[0].CommandParameter);
        Assert.Same(browser.SelectedItem, menuItems[1].CommandParameter);
    }

    [Fact]
    public void Sorting_ByNameColumn_SortsDirectoriesFirst_InBothDirections()
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

        ApplySort(source, browser.NameColumnSortComparer, ListSortDirection.Ascending);
        Assert.Equal("b_folder", GetRowModel(source, 0).Name);
        Assert.Equal("a_file.txt", GetRowModel(source, 1).Name);
        Assert.Equal("z_file.txt", GetRowModel(source, 2).Name);

        // Directories stay grouped above files in descending mode too (spec #57 user story 2);
        // only the within-group value order reverses.
        ApplySort(source, browser.NameColumnSortComparer, ListSortDirection.Descending);
        Assert.Equal("b_folder", GetRowModel(source, 0).Name);
        Assert.Equal("z_file.txt", GetRowModel(source, 1).Name);
        Assert.Equal("a_file.txt", GetRowModel(source, 2).Name);
    }

    [Fact]
    public void Sorting_BySizeColumn_SortsDirectoriesFirst_InBothDirections()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("dir/item.txt", 300, 150, null, false), // directory aggregate = 300
            new("medium.txt", 500, 250, null, false),
            new("large.txt", 1000, 500, null, false),
            new("small.txt", 100, 50, null, false)
        };

        browser.LoadEntries("test.zip", entries);
        var source = browser.GridSource!;

        ApplySort(source, browser.SizeColumnSortComparer, ListSortDirection.Ascending);
        Assert.Equal("dir", GetRowModel(source, 0).Name);
        Assert.Equal("small.txt", GetRowModel(source, 1).Name);
        Assert.Equal("medium.txt", GetRowModel(source, 2).Name);
        Assert.Equal("large.txt", GetRowModel(source, 3).Name);

        ApplySort(source, browser.SizeColumnSortComparer, ListSortDirection.Descending);
        Assert.Equal("dir", GetRowModel(source, 0).Name);
        Assert.Equal("large.txt", GetRowModel(source, 1).Name);
        Assert.Equal("medium.txt", GetRowModel(source, 2).Name);
        Assert.Equal("small.txt", GetRowModel(source, 3).Name);
    }

    [Fact]
    public void Sorting_ByCompressedColumn_SortsDirectoriesFirst_InBothDirections()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("dir/item.txt", 1000, 150, null, false), // directory compressed aggregate = 150
            new("fileB.txt", 1000, 300, null, false),
            new("fileA.txt", 1000, 100, null, false),
            new("fileC.txt", 1000, 600, null, false)
        };

        browser.LoadEntries("test.zip", entries);
        var source = browser.GridSource!;

        ApplySort(source, browser.CompressedColumnSortComparer, ListSortDirection.Ascending);
        Assert.Equal("dir", GetRowModel(source, 0).Name);
        Assert.Equal("fileA.txt", GetRowModel(source, 1).Name);
        Assert.Equal("fileB.txt", GetRowModel(source, 2).Name);
        Assert.Equal("fileC.txt", GetRowModel(source, 3).Name);

        ApplySort(source, browser.CompressedColumnSortComparer, ListSortDirection.Descending);
        Assert.Equal("dir", GetRowModel(source, 0).Name);
        Assert.Equal("fileC.txt", GetRowModel(source, 1).Name);
        Assert.Equal("fileB.txt", GetRowModel(source, 2).Name);
        Assert.Equal("fileA.txt", GetRowModel(source, 3).Name);
    }

    [Fact]
    public void Sorting_ByModifiedColumn_SortsDirectoriesFirst_InBothDirections()
    {
        var browser = new ArchiveBrowserViewModel();
        var date1 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var date2 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var date3 = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        var entries = new List<ArchiveEntry>
        {
            new("dir/", 0, null, date2, true), // explicit directory entry
            new("file2.txt", 100, 50, date2, false),
            new("file1.txt", 100, 50, date1, false),
            new("file3.txt", 100, 50, date3, false)
        };

        browser.LoadEntries("test.zip", entries);
        var source = browser.GridSource!;

        ApplySort(source, browser.ModifiedColumnSortComparer, ListSortDirection.Ascending);
        Assert.Equal("dir", GetRowModel(source, 0).Name);
        Assert.Equal("file1.txt", GetRowModel(source, 1).Name);
        Assert.Equal("file2.txt", GetRowModel(source, 2).Name);
        Assert.Equal("file3.txt", GetRowModel(source, 3).Name);

        ApplySort(source, browser.ModifiedColumnSortComparer, ListSortDirection.Descending);
        Assert.Equal("dir", GetRowModel(source, 0).Name);
        Assert.Equal("file3.txt", GetRowModel(source, 1).Name);
        Assert.Equal("file2.txt", GetRowModel(source, 2).Name);
        Assert.Equal("file1.txt", GetRowModel(source, 3).Name);
    }

    [Fact]
    public void Sorting_ByAttributesColumn_SortsDirectoriesFirst_InBothDirections()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("zdir/", 0, null, null, true, false, "d---------"),
            new("a_file.txt", 100, 50, null, false, false, "a---------"),
            new("m_file.txt", 100, 50, null, false, false, "m---------")
        };

        browser.LoadEntries("test.zip", entries);
        var source = browser.GridSource!;

        ApplySort(source, browser.AttributesColumnSortComparer, ListSortDirection.Ascending);
        Assert.Equal("zdir", GetRowModel(source, 0).Name);
        Assert.Equal("a_file.txt", GetRowModel(source, 1).Name);
        Assert.Equal("m_file.txt", GetRowModel(source, 2).Name);

        ApplySort(source, browser.AttributesColumnSortComparer, ListSortDirection.Descending);
        Assert.Equal("zdir", GetRowModel(source, 0).Name);
        Assert.Equal("m_file.txt", GetRowModel(source, 1).Name);
        Assert.Equal("a_file.txt", GetRowModel(source, 2).Name);
    }

    [Fact]
    public void Sorting_ByRatioColumn_SortsNumericallyOnRatioValue()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("high.txt", 1000, 900, null, false),   // 90.0%
            new("low.txt", 1000, 100, null, false),    // 10.0%
            new("folder/item.txt", 1000, 500, null, false), // directory aggregate
            new("mid.txt", 1000, 500, null, false)     // 50.0%
        };

        browser.LoadEntries("test.zip", entries);
        var source = browser.GridSource!;

        // Directories always sort above files ascending; files order by numeric ratio.
        ApplySort(source, browser.RatioColumnSortComparer, ListSortDirection.Ascending);
        Assert.Equal("folder", GetRowModel(source, 0).Name);
        Assert.Equal("low.txt", GetRowModel(source, 1).Name);
        Assert.Equal("mid.txt", GetRowModel(source, 2).Name);
        Assert.Equal("high.txt", GetRowModel(source, 3).Name);

        // Descending keeps directories grouped above files; only the file ratio order reverses.
        ApplySort(source, browser.RatioColumnSortComparer, ListSortDirection.Descending);
        Assert.Equal("folder", GetRowModel(source, 0).Name);
        Assert.Equal("high.txt", GetRowModel(source, 1).Name);
        Assert.Equal("mid.txt", GetRowModel(source, 2).Name);
        Assert.Equal("low.txt", GetRowModel(source, 3).Name);
    }

    [Fact]
    public void Sorting_ByRatioColumn_OrdersFilesWithoutRatioAfterFilesWithRatio()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("no_ratio.bin", 1000, null, null, false), // CompressedSize null -> "-"
            new("ratio.txt", 1000, 200, null, false)      // 20.0%
        };

        browser.LoadEntries("test.zip", entries);
        var source = browser.GridSource!;

        ApplySort(source, browser.RatioColumnSortComparer, ListSortDirection.Ascending);
        Assert.Equal("ratio.txt", GetRowModel(source, 0).Name);
        Assert.Equal("no_ratio.bin", GetRowModel(source, 1).Name);
    }

    private static ArchiveItemViewModel GetRowModel(IHierarchicalModel source, int index)
    {
        return (ArchiveItemViewModel)source.Flattened[index].Item;
    }

    /// <summary>
    /// Applies a column comparer to the hierarchical model the same way the ProDataGrid
    /// column-sort framework does: it sets the comparer's direction, then ascending uses the
    /// comparer directly while descending negates its result.
    /// </summary>
    private static void ApplySort(IHierarchicalModel source, ArchiveItemSortComparer comparer, ListSortDirection direction)
    {
        comparer.Direction = direction;
        source.ApplySiblingComparer(Comparer<object>.Create((a, b) =>
        {
            int result = comparer.Compare(a, b);
            return direction == ListSortDirection.Descending ? -result : result;
        }), recursive: true);
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
    public void FilterText_AutoExpandsDirectories_InModelFlattenedRows()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("documents/reports/q1.pdf", 1000, 500, null, false),
            new("images/photo.png", 2000, 1800, null, false)
        };

        browser.LoadEntries("archive.zrus", entries);
        Assert.Equal(2, browser.GridSource!.Flattened.Count); // documents + images (collapsed)

        browser.FilterText = "q1";
        var source = browser.GridSource!;
        // Filtered directories auto-expand so the matching descendant is visible.
        Assert.Equal(3, source.Flattened.Count);
        Assert.Equal("documents", ((ArchiveItemViewModel)source.Flattened[0].Item).Name);
        Assert.Equal("reports", ((ArchiveItemViewModel)source.Flattened[1].Item).Name);
        Assert.Equal("q1.pdf", ((ArchiveItemViewModel)source.Flattened[2].Item).Name);
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
    public void GridSource_FlattenedRows_ReflectExpansionAndCollapse()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("folder/subfolder/file.txt", 100, 50, null, false)
        };

        browser.LoadEntries("test.zip", entries);
        var source = browser.GridSource!;

        // Roots are visible; descendants appear only when their ancestors expand.
        Assert.Single(source.Flattened);
        Assert.Equal("folder", ((ArchiveItemViewModel)source.Flattened[0].Item).Name);

        var folder = browser.RootItems[0];
        folder.IsExpanded = true;
        Assert.Equal(2, source.Flattened.Count);
        Assert.Equal("subfolder", ((ArchiveItemViewModel)source.Flattened[1].Item).Name);

        folder.Children[0].IsExpanded = true;
        Assert.Equal(3, source.Flattened.Count);
        Assert.Equal("file.txt", ((ArchiveItemViewModel)source.Flattened[2].Item).Name);

        folder.IsExpanded = false;
        Assert.Single(source.Flattened);
        Assert.Equal("folder", ((ArchiveItemViewModel)source.Flattened[0].Item).Name);
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

    [Fact]
    public void LoadEntries_BeyondEntryCountCap_SetsErrorBanner_AndRefusesTreeConstruction()
    {
        var browser = new ArchiveBrowserViewModel { EntryCountCap = 2 };
        var entries = new List<ArchiveEntry>
        {
            new("file1.txt", 1, 1, null, false),
            new("file2.txt", 1, 1, null, false),
            new("file3.txt", 1, 1, null, false)
        };

        browser.LoadEntries("bomb.zip", entries);

        Assert.True(browser.HasLoadError);
        Assert.Contains("safety limit", browser.LoadErrorMessage);
        Assert.Null(browser.GridSource);
        Assert.Empty(browser.RootItems);
    }

    [Fact]
    public void LoadEntries_WithinEntryCountCap_ClearsErrorAndBuildsTree()
    {
        var browser = new ArchiveBrowserViewModel { EntryCountCap = 10 };
        var entries = new List<ArchiveEntry>
        {
            new("file1.txt", 1, 1, null, false)
        };

        browser.LoadEntries("ok.zip", entries);

        Assert.False(browser.HasLoadError);
        Assert.Equal(string.Empty, browser.LoadErrorMessage);
        Assert.NotNull(browser.GridSource);
    }

    [Fact]
    public void ExtractionSettings_Defaults_MatchSafeArchiveExtractor()
    {
        var browser = new ArchiveBrowserViewModel();
        var settings = browser.ExtractionSettings;

        Assert.Equal("64.0 GB", settings.MaxUncompressedSizeText);
        Assert.Equal(1_000_000m, settings.MaxEntryCount);

        var limits = settings.BuildLimits();
        Assert.Equal(64L * 1024 * 1024 * 1024, limits.MaxCumulativeUncompressedBytes);
        Assert.Equal(1_000_000, limits.MaxEntryCount);
    }

    [Fact]
    public void ExtractionSettings_ZeroOrInvalid_MeansUnlimited()
    {
        var settings = new ExtractionSettingsViewModel
        {
            MaxUncompressedSizeText = "0",
            MaxEntryCount = 0
        };

        var limits = settings.BuildLimits();
        Assert.Null(limits.MaxCumulativeUncompressedBytes);
        Assert.Null(limits.MaxEntryCount);

        settings.MaxUncompressedSizeText = "not-a-size";
        var limits2 = settings.BuildLimits();
        Assert.Null(limits2.MaxCumulativeUncompressedBytes);
    }

    [Fact]
    public void UpdateBreadcrumbs_SanitizesControlBytesInSegments()
    {
        char esc = (char)0x1b;
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new($"esc{esc}seg/file.txt", 100, 50, null, false)
        };

        browser.LoadEntries("test.zip", entries);

        // Tree nodes sanitize the relative path, so navigation uses the cleaned path.
        var item = browser.FindItemByPath("escseg/file.txt");
        Assert.NotNull(item);
        browser.SelectedItem = item;

        Assert.Equal(3, browser.Breadcrumbs.Count);
        Assert.Equal("Archive", browser.Breadcrumbs[0].Name);
        Assert.Equal("escseg", browser.Breadcrumbs[1].Name);
        Assert.Equal("file.txt", browser.Breadcrumbs[2].Name);
        Assert.All(browser.Breadcrumbs, b => Assert.DoesNotContain(esc, b.Name));
    }

    [Fact]
    public void Breadcrumbs_OnFolderExpansion_TrackTheExpandedNode()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("src/RusZip.Desktop/App.axaml", 100, 50, null, false)
        };
        browser.LoadEntries("test.zip", entries);

        // Nothing selected and nothing expanded: breadcrumbs show the archive root.
        Assert.Single(browser.Breadcrumbs);
        Assert.Equal("Archive", browser.Breadcrumbs[0].Name);
        Assert.True(browser.Breadcrumbs[0].IsLast);

        // Expanding "src" makes it the navigation context (F-39).
        var src = browser.FindItemByPath("src")!;
        src.IsExpanded = true;

        Assert.Equal(2, browser.Breadcrumbs.Count);
        Assert.Equal("Archive", browser.Breadcrumbs[0].Name);
        Assert.Equal("src", browser.Breadcrumbs[1].Name);
        Assert.True(browser.Breadcrumbs[1].IsLast);

        // Expanding a descendant pushes the trail deeper.
        var desktop = browser.FindItemByPath("src/RusZip.Desktop")!;
        desktop.IsExpanded = true;

        Assert.Equal(3, browser.Breadcrumbs.Count);
        Assert.Equal("RusZip.Desktop", browser.Breadcrumbs[2].Name);
        Assert.True(browser.Breadcrumbs[2].IsLast);
        Assert.Equal(Avalonia.Media.FontWeight.SemiBold, browser.Breadcrumbs[2].FontWeight);
    }

    [Fact]
    public void Breadcrumbs_OnFolderCollapse_RevertToDeepestExpandedAncestor()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("src/RusZip.Desktop/App.axaml", 100, 50, null, false)
        };
        browser.LoadEntries("test.zip", entries);

        var src = browser.FindItemByPath("src")!;
        var desktop = browser.FindItemByPath("src/RusZip.Desktop")!;
        src.IsExpanded = true;
        desktop.IsExpanded = true;

        Assert.Equal(3, browser.Breadcrumbs.Count);
        Assert.Equal("RusZip.Desktop", browser.Breadcrumbs[2].Name);

        // Collapsing the deepest expanded folder reverts to its still-expanded parent.
        desktop.IsExpanded = false;
        Assert.Equal(2, browser.Breadcrumbs.Count);
        Assert.Equal("src", browser.Breadcrumbs[1].Name);
        Assert.True(browser.Breadcrumbs[1].IsLast);

        // Collapsing the parent reverts all the way to the root.
        src.IsExpanded = false;
        Assert.Single(browser.Breadcrumbs);
        Assert.Equal("Archive", browser.Breadcrumbs[0].Name);
        Assert.True(browser.Breadcrumbs[0].IsLast);
    }

    [Fact]
    public void Breadcrumbs_SelectionTakesPriorityOverExpansionAnchor()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("folder/selected.txt", 100, 50, null, false),
            new("folder/other.txt", 100, 50, null, false)
        };
        browser.LoadEntries("test.zip", entries);

        var selected = browser.FindItemByPath("folder/selected.txt")!;
        browser.SelectedItem = selected;

        Assert.Equal(3, browser.Breadcrumbs.Count);
        Assert.Equal("selected.txt", browser.Breadcrumbs[2].Name);
        Assert.True(browser.Breadcrumbs[2].IsLast);

        // Expanding a sibling folder elsewhere must not override the selected node's trail.
        var folder = browser.FindItemByPath("folder")!;
        folder.IsExpanded = true;

        Assert.Equal("selected.txt", browser.Breadcrumbs[2].Name);
        Assert.Equal(3, browser.Breadcrumbs.Count);
    }

    [Fact]
    public void Breadcrumbs_SelectingNullAfterExpansion_FallsBackToExpansionAnchor()
    {
        var browser = new ArchiveBrowserViewModel();
        var entries = new List<ArchiveEntry>
        {
            new("folder/file.txt", 100, 50, null, false)
        };
        browser.LoadEntries("test.zip", entries);

        var folder = browser.FindItemByPath("folder")!;
        folder.IsExpanded = true;
        Assert.Equal(2, browser.Breadcrumbs.Count);

        // Explicitly clearing the selection keeps the expanded folder as the context.
        browser.SelectedItem = null;
        Assert.Equal(2, browser.Breadcrumbs.Count);
        Assert.Equal("folder", browser.Breadcrumbs[1].Name);
        Assert.True(browser.Breadcrumbs[1].IsLast);
    }
}

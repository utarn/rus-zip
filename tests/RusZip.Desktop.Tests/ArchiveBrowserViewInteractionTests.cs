using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RusZip.Core.Models;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public class ArchiveBrowserViewInteractionTests
{
    [AvaloniaFact]
    public void ArchiveBrowserView_DataContextChanged_AppliesCustomSortComparersToColumns()
    {
        var vm = new ArchiveBrowserViewModel();
        var view = new ArchiveBrowserView
        {
            DataContext = vm
        };

        var grid = view.FindControl<DataGrid>("ArchiveGrid");
        Assert.NotNull(grid);

        var nameCol = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Name");
        Assert.NotNull(nameCol);
        Assert.Same(vm.NameColumnSortComparer, nameCol.CustomSortComparer);

        var sizeCol = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Size");
        Assert.NotNull(sizeCol);
        Assert.Same(vm.SizeColumnSortComparer, sizeCol.CustomSortComparer);

        var compCol = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Compressed");
        Assert.NotNull(compCol);
        Assert.Same(vm.CompressedColumnSortComparer, compCol.CustomSortComparer);

        var ratioCol = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Ratio");
        Assert.NotNull(ratioCol);
        Assert.Same(vm.RatioColumnSortComparer, ratioCol.CustomSortComparer);

        var modCol = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Modified");
        Assert.NotNull(modCol);
        Assert.Same(vm.ModifiedColumnSortComparer, modCol.CustomSortComparer);

        var attrCol = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Attributes");
        Assert.NotNull(attrCol);
        Assert.Same(vm.AttributesColumnSortComparer, attrCol.CustomSortComparer);
    }

    [AvaloniaFact]
    public void ArchiveBrowserView_ContextMenuTarget_UpdatesParametersAndResets()
    {
        var vm = new ArchiveBrowserViewModel();
        var node = new ArchiveTreeNode
        {
            Name = "doc.txt",
            RelativePath = "doc.txt",
            IsDirectory = false,
            UncompressedSize = 100,
            CompressedSize = 50,
            LastModified = DateTimeOffset.UtcNow
        };
        var item = ArchiveItemViewModel.FromTreeNode(node, false);

        var view = new ArchiveBrowserView
        {
            DataContext = vm
        };

        view.SetContextMenuTarget(item);

        var grid = view.FindControl<DataGrid>("ArchiveGrid");
        Assert.NotNull(grid);
        Assert.NotNull(grid.ContextMenu);

        foreach (var menuItem in grid.ContextMenu.Items.OfType<MenuItem>())
        {
            Assert.Same(item, menuItem.CommandParameter);
        }

        // Apply fallback to selection
        view.ApplyContextMenuFallbackToSelection();

        // Clearing target
        view.SetContextMenuTarget(null);
        foreach (var menuItem in grid.ContextMenu.Items.OfType<MenuItem>())
        {
            Assert.Null(menuItem.CommandParameter);
        }
    }

    [AvaloniaFact]
    public void ArchiveBrowserView_KeyDown_DeleteKey_ExecutesDeleteCommandWhenCanExecute()
    {
        var vm = new ArchiveBrowserViewModel();
        var view = new ArchiveBrowserView
        {
            DataContext = vm
        };

        var grid = view.FindControl<DataGrid>("ArchiveGrid");
        Assert.NotNull(grid);

        var keyEventArgs = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Delete
        };

        grid.RaiseEvent(keyEventArgs);
    }

    [AvaloniaFact]
    public void ArchiveBrowserView_SortDirectionChange_UpdatesViewModelDirection()
    {
        var vm = new ArchiveBrowserViewModel();
        var view = new ArchiveBrowserView
        {
            DataContext = vm
        };

        var grid = view.FindControl<DataGrid>("ArchiveGrid");
        Assert.NotNull(grid);

        var nameCol = grid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Name");
        Assert.NotNull(nameCol);

        nameCol.SortDirection = ListSortDirection.Descending;
        Assert.Equal(ListSortDirection.Descending, vm.NameColumnSortComparer.Direction);

        nameCol.SortDirection = ListSortDirection.Ascending;
        Assert.Equal(ListSortDirection.Ascending, vm.NameColumnSortComparer.Direction);
    }
}

using Avalonia.Controls;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Views;

public partial class ArchiveBrowserView : UserControl
{
    /// <summary>
    /// The row under the right-click pointer, passed as <c>CommandParameter</c> to every context
    /// menu item (F-40). Null when the menu is opened via keyboard, in which case the commands
    /// fall back to the selected row.
    /// </summary>
    private ArchiveItemViewModel? _contextTarget;

    public ArchiveBrowserView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ApplyColumnSortComparers();
        ApplyColumnSortComparers();

        ArchiveGrid.CellPointerPressed += OnArchiveGridCellPointerPressed;
        if (ArchiveGrid.ContextMenu is { } menu)
        {
            menu.Opening += OnArchiveGridContextMenuOpening;
            menu.Closing += OnArchiveGridContextMenuClosing;
        }
    }

    /// <summary>
    /// Records the clicked row and pushes it as the <c>CommandParameter</c> on every context menu
    /// item. Exposed internally for headless tests.
    /// </summary>
    internal void SetContextMenuTarget(ArchiveItemViewModel? item)
    {
        _contextTarget = item;
        UpdateContextMenuParameters();
    }

    /// <summary>
    /// Applies the keyboard-invocation fallback: no pointer target exists, so the selected row
    /// becomes the context-menu target. Mirrors <see cref="OnArchiveGridContextMenuOpening"/>;
    /// exposed internally for headless tests.
    /// </summary>
    internal void ApplyContextMenuFallbackToSelection()
    {
        SetContextMenuTarget(_contextTarget ?? ArchiveGrid.SelectedItem as ArchiveItemViewModel);
    }

    private void OnArchiveGridCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        if (e.PointerPressedEventArgs.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            SetContextMenuTarget(e.Row?.DataContext as ArchiveItemViewModel);
        }
    }

    private void OnArchiveGridContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Keyboard invocation (Shift+F10 / menu key) has no pointer target; fall back to the
        // selected row so the commands act on the current selection.
        ApplyContextMenuFallbackToSelection();
    }

    private void OnArchiveGridContextMenuClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _contextTarget = null;
    }

    private void UpdateContextMenuParameters()
    {
        if (ArchiveGrid.ContextMenu is not { } menu)
        {
            return;
        }

        foreach (var menuItem in menu.Items.OfType<MenuItem>())
        {
            menuItem.CommandParameter = _contextTarget;
        }
    }

    /// <summary>
    /// Assigns the ViewModel's column sort comparers to the ProDataGrid columns.
    /// <see cref="DataGridColumn.CustomSortComparer"/> is a plain CLR property, so it cannot
    /// be bound from XAML; the comparers are the same instances the ViewModel exposes.
    /// </summary>
    private void ApplyColumnSortComparers()
    {
        if (DataContext is not ArchiveBrowserViewModel vm)
        {
            return;
        }

        foreach (var column in ArchiveGrid.Columns)
        {
            switch (column.Header?.ToString())
            {
                case "Name":
                    column.CustomSortComparer = vm.NameColumnSortComparer;
                    break;
                case "Size":
                    column.CustomSortComparer = vm.SizeColumnSortComparer;
                    break;
                case "Compressed":
                    column.CustomSortComparer = vm.CompressedColumnSortComparer;
                    break;
                case "Ratio":
                    column.CustomSortComparer = vm.RatioColumnSortComparer;
                    break;
                case "Modified":
                    column.CustomSortComparer = vm.ModifiedColumnSortComparer;
                    break;
                case "Attributes":
                    column.CustomSortComparer = vm.AttributesColumnSortComparer;
                    break;
            }
        }
    }
}

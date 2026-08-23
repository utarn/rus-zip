using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

    /// <summary>
    /// Columns whose <see cref="DataGridColumn.SortDirectionProperty"/> changes are forwarded to
    /// the ViewModel. ProDataGrid syncs each column's SortDirection from its sorting model before
    /// applying the sort, so this forwarding keeps the direction-aware column comparers in sync.
    /// Re-wired on every DataContext change so earlier VMs are not mutated.
    /// </summary>
    private readonly HashSet<DataGridColumn> _sortDirectionObservedColumns = new();

    public ArchiveBrowserView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ApplyColumnSortComparers();
        ApplyColumnSortComparers();

        ArchiveGrid.CellPointerPressed += OnArchiveGridCellPointerPressed;
        ArchiveGrid.KeyDown += OnArchiveGridKeyDown;
        if (ArchiveGrid.ContextMenu is { } menu)
        {
            menu.Opening += OnArchiveGridContextMenuOpening;
            menu.Closing += OnArchiveGridContextMenuClosing;
        }
    }

    private static ArchiveItemViewModel? ExtractArchiveItem(object? obj)
    {
        if (obj is ArchiveItemViewModel item)
        {
            return item;
        }
        if (obj != null)
        {
            var prop = obj.GetType().GetProperty("Item");
            if (prop?.GetValue(obj) is ArchiveItemViewModel unwrapped)
            {
                return unwrapped;
            }
        }
        return null;
    }

    private void OnArchiveGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ArchiveBrowserViewModel vm)
        {
            return;
        }

        var selected = ArchiveGrid.SelectedItems
            .OfType<object>()
            .Select(ExtractArchiveItem)
            .Where(x => x != null)
            .Select(x => x!)
            .Distinct()
            .ToList();

        vm.SetSelectedItems(selected);
    }

    private void OnArchiveGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Delete or Key.Back && DataContext is ArchiveBrowserViewModel vm)
        {
            if (vm.DeleteSelectedCommand.CanExecute(null))
            {
                vm.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
            }
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
    /// Assigns the ViewModel's column sort comparers to the ProDataGrid columns and wires the
    /// active sort direction into them.
    /// <see cref="DataGridColumn.CustomSortComparer"/> is a plain CLR property, so it cannot be
    /// bound from XAML; the comparers are the same instances the ViewModel exposes.
    /// ProDataGrid negates the column comparer for descending sorts, so the comparers are
    /// direction-aware. The grid syncs each column's <see cref="DataGridColumn.SortDirection"/>
    /// before applying the sort; these subscriptions forward that direction to the comparers so
    /// directories stay grouped above files in both sort directions. This is the minimal
    /// code-behind hook required because neither the comparer assignment nor the direction
    /// wiring can be expressed declaratively in XAML.
    /// </summary>
    private void ApplyColumnSortComparers()
    {
        if (DataContext is not ArchiveBrowserViewModel vm)
        {
            return;
        }

        foreach (var column in _sortDirectionObservedColumns)
        {
            column.PropertyChanged -= OnArchiveGridColumnPropertyChanged;
        }
        _sortDirectionObservedColumns.Clear();

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

            // The grid sets the column's SortDirection before invoking the comparer (see
            // DataGrid.SyncColumnSortDirectionsFromModel), so forwarding it here guarantees the
            // comparer sees the current direction at comparison time.
            column.PropertyChanged += OnArchiveGridColumnPropertyChanged;
            _sortDirectionObservedColumns.Add(column);
        }
    }

    /// <summary>
    /// Forwards a column's <see cref="DataGridColumn.SortDirectionProperty"/> change into the
    /// ViewModel, which updates the direction-aware comparers.
    /// </summary>
    private void OnArchiveGridColumnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == DataGridColumn.SortDirectionProperty && DataContext is ArchiveBrowserViewModel vm)
        {
            vm.SetColumnSortDirection((ListSortDirection?)e.NewValue);
        }
    }
}

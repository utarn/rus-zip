using Avalonia.Controls;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Views;

public partial class ArchiveBrowserView : UserControl
{
    public ArchiveBrowserView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ApplyColumnSortComparers();
        ApplyColumnSortComparers();
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

using System.Collections;
using System.ComponentModel;

namespace RusZip.Desktop.ViewModels;

/// <summary>
/// Builds the <see cref="ArchiveItemSortComparer"/> instances used by the archive browser's
/// ProDataGrid columns. Directories always sort above files in BOTH sort directions; siblings
/// order by their column value (ascending, or descending via the grid's direction handling).
/// </summary>
internal static class ArchiveItemComparer
{
    public static ArchiveItemSortComparer CreateName()
        => new(item => item.Name, directoriesFirst: true, StringComparer.OrdinalIgnoreCase);

    public static ArchiveItemSortComparer CreateSize()
        => new(item => item.UncompressedSize, directoriesFirst: true, Comparer<long>.Default);

    public static ArchiveItemSortComparer CreateCompressed()
        => new(item => item.CompressedSize ?? 0, directoriesFirst: true, Comparer<long>.Default);

    public static ArchiveItemSortComparer CreateModified()
        => new(item => item.LastModified ?? DateTimeOffset.MinValue, directoriesFirst: true, Comparer<DateTimeOffset>.Default);

    public static ArchiveItemSortComparer CreateRatio()
        => new(item => item.RatioValue, directoriesFirst: true, Comparer<double>.Default);

    public static ArchiveItemSortComparer CreateAttributes()
        => new(item => item.Attributes, directoriesFirst: true, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Compares <see cref="ArchiveItemViewModel"/> siblings for the archive browser's ProDataGrid
/// column sort.
/// </summary>
/// <remarks>
/// <para>
/// The ProDataGrid 12.1 sort framework (<c>HierarchicalSiblingComparerBuilder</c>) negates the
/// column comparer's result for descending sorts. To keep directories grouped ABOVE files in
/// BOTH directions, this comparer is direction-aware: when <see cref="Direction"/> is
/// descending it inverts only the directory-vs-file grouping key, so the framework's negation
/// restores directories-first, while the column value is compared ascending so the framework's
/// negation yields descending values within each group.
/// </para>
/// <para>
/// The grid framework sets each column's <c>SortDirection</c> before invoking the comparer
/// (see <c>DataGrid.SyncColumnSortDirectionsFromModel</c>). The view code-behind pushes that
/// direction into <see cref="Direction"/> via the ViewModel.
/// </para>
/// </remarks>
public sealed class ArchiveItemSortComparer : IComparer
{
    private readonly Func<ArchiveItemViewModel, object?> _valueSelector;
    private readonly bool _directoriesFirst;
    private readonly IComparer _valueComparer;

    public ArchiveItemSortComparer(Func<ArchiveItemViewModel, object?> valueSelector, bool directoriesFirst, IComparer valueComparer)
    {
        _valueSelector = valueSelector;
        _directoriesFirst = directoriesFirst;
        _valueComparer = valueComparer;
    }

    /// <summary>
    /// Current sort direction. Ascending keeps directories first and orders siblings by their
    /// column value ascending; descending inverts the directory grouping so that, after the
    /// grid framework negates the result, directories remain first and values order descending.
    /// </summary>
    public ListSortDirection Direction { get; set; } = ListSortDirection.Ascending;

    public int Compare(object? x, object? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        var a = x as ArchiveItemViewModel;
        var b = y as ArchiveItemViewModel;
        if (a == null)
        {
            return b == null ? 0 : -1;
        }

        if (b == null)
        {
            return 1;
        }

        if (_directoriesFirst && a.IsDirectory != b.IsDirectory)
        {
            // Directories sort above files ascending. For descending, the grid framework
            // negates the whole comparer result, so invert the grouping key here — the
            // negation then restores directories-first while reversing only the values.
            int dirFirst = a.IsDirectory ? -1 : 1;
            return Direction == ListSortDirection.Descending ? -dirFirst : dirFirst;
        }

        return _valueComparer.Compare(_valueSelector(a), _valueSelector(b));
    }
}

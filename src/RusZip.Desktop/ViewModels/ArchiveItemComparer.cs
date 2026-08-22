using System.Collections;

namespace RusZip.Desktop.ViewModels;

/// <summary>
/// Builds the <see cref="IComparer"/> instances used by the archive browser's ProDataGrid
/// columns. Preserves the TreeDataGrid-era sort semantics: directories always sort above
/// files (except for the Attributes column), and siblings order by their column value.
/// The DataGrid column sort framework negates the comparer result for descending sorts;
/// the ascending comparers here match the previous TreeDataGrid behavior.
/// </summary>
internal static class ArchiveItemComparer
{
    public static IComparer CreateName()
        => new ValueComparer(item => item.Name, directoriesFirst: true, StringComparer.OrdinalIgnoreCase);

    public static IComparer CreateSize()
        => new ValueComparer(item => item.UncompressedSize, directoriesFirst: true, Comparer<long>.Default);

    public static IComparer CreateCompressed()
        => new ValueComparer(item => item.CompressedSize ?? 0, directoriesFirst: true, Comparer<long>.Default);

    public static IComparer CreateModified()
        => new ValueComparer(item => item.LastModified ?? DateTimeOffset.MinValue, directoriesFirst: true, Comparer<DateTimeOffset>.Default);

    public static IComparer CreateAttributes()
        => new ValueComparer(item => item.Attributes, directoriesFirst: false, StringComparer.OrdinalIgnoreCase);

    private sealed class ValueComparer : IComparer
    {
        private readonly Func<ArchiveItemViewModel, object?> _valueSelector;
        private readonly bool _directoriesFirst;
        private readonly IComparer _valueComparer;

        public ValueComparer(Func<ArchiveItemViewModel, object?> valueSelector, bool directoriesFirst, IComparer valueComparer)
        {
            _valueSelector = valueSelector;
            _directoriesFirst = directoriesFirst;
            _valueComparer = valueComparer;
        }

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
                return a.IsDirectory ? -1 : 1;
            }

            return _valueComparer.Compare(_valueSelector(a), _valueSelector(b));
        }
    }
}

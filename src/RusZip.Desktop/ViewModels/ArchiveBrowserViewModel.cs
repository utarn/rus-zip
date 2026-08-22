using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using RusZip.Core.Models;

namespace RusZip.Desktop.ViewModels;

public partial class ArchiveBrowserViewModel : ObservableObject
{
    [ObservableProperty] private HierarchicalTreeDataGridSource<ArchiveItemViewModel>? _gridSource;
    [ObservableProperty] private ObservableCollection<ArchiveItemViewModel> _rootItems = [];
    [ObservableProperty] private string _loadedArchivePath = string.Empty;
    [ObservableProperty] private int _totalEntries;
    [ObservableProperty] private long _totalUncompressedBytes;

    public void LoadEntries(string archivePath, IReadOnlyList<ArchiveEntry> entries)
    {
        LoadedArchivePath = archivePath;
        TotalEntries = entries.Count;
        TotalUncompressedBytes = entries.Sum(e => e.UncompressedSize);

        RootItems = BuildTree(entries);

        GridSource = new HierarchicalTreeDataGridSource<ArchiveItemViewModel>(RootItems)
        {
            Columns =
            {
                new HierarchicalExpanderColumn<ArchiveItemViewModel>(
                    new TemplateColumn<ArchiveItemViewModel>(
                        "Name",
                        new FuncDataTemplate<ArchiveItemViewModel>((item, _) =>
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 6,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        VerticalAlignment = VerticalAlignment.Center,
                                        [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(ArchiveItemViewModel.IconDisplay))
                                    },
                                    new TextBlock
                                    {
                                        VerticalAlignment = VerticalAlignment.Center,
                                        [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(ArchiveItemViewModel.Name))
                                    }
                                }
                            }
                        )
                    ),
                    x => x.Children,
                    x => x.HasChildren,
                    x => x.IsExpanded
                ),

                new TextColumn<ArchiveItemViewModel, string>(
                    "Size",
                    x => x.FormattedUncompressedSize,
                    options: new TextColumnOptions<ArchiveItemViewModel>
                    {
                        TextAlignment = Avalonia.Media.TextAlignment.Right
                    }
                ),

                new TextColumn<ArchiveItemViewModel, string>(
                    "Modified",
                    x => x.FormattedLastModified
                )
            }
        };
    }

    private static ObservableCollection<ArchiveItemViewModel> BuildTree(IReadOnlyList<ArchiveEntry> entries)
    {
        var rootNodes = new ObservableCollection<ArchiveItemViewModel>();
        var lookup = new Dictionary<string, ArchiveItemViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries.OrderBy(e => e.RelativePath))
        {
            var segments = entry.RelativePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            string currentPath = string.Empty;
            ArchiveItemViewModel? parent = null;

            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                currentPath = string.IsNullOrEmpty(currentPath) ? segment : $"{currentPath}/{segment}";
                bool isLeaf = (i == segments.Length - 1) && !entry.IsDirectory;

                if (!lookup.TryGetValue(currentPath, out var node))
                {
                    node = new ArchiveItemViewModel
                    {
                        Name = segment,
                        RelativePath = currentPath,
                        ItemType = isLeaf ? ArchiveItemType.File : ArchiveItemType.Directory,
                        UncompressedSize = isLeaf ? entry.UncompressedSize : 0,
                        CompressedSize = isLeaf ? entry.CompressedSize : 0,
                        LastModified = entry.LastModified,
                        Attributes = entry.Attributes
                    };

                    lookup[currentPath] = node;

                    if (parent == null)
                        rootNodes.Add(node);
                    else
                        parent.Children.Add(node);
                }

                if (!isLeaf && node != null)
                {
                    node.UncompressedSize += entry.UncompressedSize;
                }

                parent = node;
            }
        }

        return rootNodes;
    }
}

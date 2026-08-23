using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RusZip.Core.Models;

namespace RusZip.Desktop.ViewModels;

public partial class StagedSourceItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKey))]
    [NotifyPropertyChangedFor(nameof(IconDisplay))]
    [NotifyPropertyChangedFor(nameof(IconGeometry))]
    private string _name = string.Empty;

    [ObservableProperty] private string _fullPath = string.Empty;
    [ObservableProperty] private string _relativePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKey))]
    [NotifyPropertyChangedFor(nameof(IconDisplay))]
    [NotifyPropertyChangedFor(nameof(IconGeometry))]
    [NotifyPropertyChangedFor(nameof(FormattedSize))]
    [NotifyPropertyChangedFor(nameof(FormattedUncompressedSize))]
    private bool _isDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedSize))]
    [NotifyPropertyChangedFor(nameof(FormattedUncompressedSize))]
    private long _size;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedLastModified))]
    private DateTimeOffset? _lastModified;

    [ObservableProperty] private string _attributes = string.Empty;
    [ObservableProperty] private bool _isExcluded;
    [ObservableProperty] private bool _isExpanded;

    public ObservableCollection<StagedSourceItemViewModel> Children { get; } = [];
    public StagedSourceItemViewModel? Parent { get; set; }

    public bool HasChildren => Children.Count > 0;

    public string FormattedSize => IsDirectory
        ? (Size > 0 ? DataMetricsFormatter.FormatBytes(Size) : "-")
        : DataMetricsFormatter.FormatBytes(Size);

    public string FormattedUncompressedSize => FormattedSize;

    public string FormattedLastModified => LastModified.HasValue ? LastModified.Value.ToString("yyyy-MM-dd HH:mm") : "-";

    public string IconKey => FileIconCategorizer.GetIconKey(Name, IsDirectory);

    public string IconDisplay => IsDirectory ? "📁" : FileIconCategorizer.GetFileIcon(Name);

    public StreamGeometry? IconGeometry
    {
        get
        {
            if (Application.Current != null && Application.Current.TryGetResource(IconKey, null, out var res) && res is StreamGeometry geom)
            {
                return geom;
            }
            return null;
        }
    }

    private bool _isUpdatingExclusion;

    public void SetExcluded(bool excluded, bool cascade = true)
    {
        if (_isUpdatingExclusion) return;

        _isUpdatingExclusion = true;
        try
        {
            IsExcluded = excluded;
            if (cascade)
            {
                foreach (var child in Children)
                {
                    child.SetExcluded(excluded, cascade: true);
                }
            }
            if (!excluded && Parent != null && Parent.IsExcluded)
            {
                Parent.SetExcluded(false, cascade: false);
            }
        }
        finally
        {
            _isUpdatingExclusion = false;
        }
    }

    partial void OnIsExcludedChanged(bool value)
    {
        if (!_isUpdatingExclusion)
        {
            SetExcluded(value, cascade: true);
        }
    }

    public static StagedSourceItemViewModel FromFileSystem(string path, StagedSourceItemViewModel? parent = null, string? relativePrefix = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new StagedSourceItemViewModel { Parent = parent };
        }

        var fullPath = Path.GetFullPath(path);
        var isDir = Directory.Exists(fullPath);
        var isFile = File.Exists(fullPath);

        if (isDir)
        {
            var di = new DirectoryInfo(fullPath);
            var name = di.Name;
            var relPath = string.IsNullOrEmpty(relativePrefix) ? name : $"{relativePrefix}/{name}";
            var vm = new StagedSourceItemViewModel
            {
                Name = name,
                FullPath = fullPath,
                RelativePath = relPath,
                IsDirectory = true,
                LastModified = di.LastWriteTimeUtc,
                Attributes = di.Attributes.ToString(),
                Parent = parent
            };

            try
            {
                foreach (var subDir in di.EnumerateDirectories())
                {
                    var childDirVm = FromFileSystem(subDir.FullName, parent: vm, relativePrefix: relPath);
                    vm.Children.Add(childDirVm);
                }

                foreach (var file in di.EnumerateFiles())
                {
                    var fileRelPath = $"{relPath}/{file.Name}";
                    var fileVm = new StagedSourceItemViewModel
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        RelativePath = fileRelPath,
                        IsDirectory = false,
                        Size = file.Length,
                        LastModified = file.LastWriteTimeUtc,
                        Attributes = file.Attributes.ToString(),
                        Parent = vm
                    };
                    vm.Children.Add(fileVm);
                }
            }
            catch (Exception)
            {
                // Ignore access errors during directory enumeration
            }

            vm.Size = vm.Children.Sum(c => c.Size);
            return vm;
        }
        else if (isFile)
        {
            var fi = new FileInfo(fullPath);
            var name = fi.Name;
            var relPath = string.IsNullOrEmpty(relativePrefix) ? name : $"{relativePrefix}/{name}";
            return new StagedSourceItemViewModel
            {
                Name = name,
                FullPath = fullPath,
                RelativePath = relPath,
                IsDirectory = false,
                Size = fi.Length,
                LastModified = fi.LastWriteTimeUtc,
                Attributes = fi.Attributes.ToString(),
                Parent = parent
            };
        }
        else
        {
            // Non-existent path fallback (for unit tests / mock paths)
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) name = path;
            var relPath = string.IsNullOrEmpty(relativePrefix) ? name : $"{relativePrefix}/{name}";
            return new StagedSourceItemViewModel
            {
                Name = name,
                FullPath = path,
                RelativePath = relPath,
                IsDirectory = false,
                Parent = parent
            };
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;

namespace RusZip.Desktop.ViewModels;

public sealed partial class ArchivePropertiesViewModel : ObservableObject
{
    public string ArchivePath { get; set; } = string.Empty;
    public string ArchiveFileName => Path.GetFileName(ArchivePath);
    public string ContainerFormat { get; set; } = string.Empty;
    public string CompressionMethod { get; set; } = string.Empty;

    public long TotalUncompressedSize { get; set; }
    public long TotalCompressedSize { get; set; }
    public string FormattedUncompressedSize => DataMetricsFormatter.FormatBytes(TotalUncompressedSize);
    public string FormattedCompressedSize => DataMetricsFormatter.FormatBytes(TotalCompressedSize);
    public string FormattedCompressionRatio => DataMetricsFormatter.FormatRatio(TotalCompressedSize, TotalUncompressedSize);

    public int TotalFiles { get; set; }
    public int TotalDirectories { get; set; }
    public int TotalEntries => TotalFiles + TotalDirectories;

    public bool HasSelectedItem => !string.IsNullOrEmpty(SelectedItemRelativePath);
    public string SelectedItemName { get; set; } = string.Empty;
    public string SelectedItemRelativePath { get; set; } = string.Empty;
    public string SelectedItemType { get; set; } = string.Empty;
    public long SelectedItemUncompressedSize { get; set; }
    public long? SelectedItemCompressedSize { get; set; }
    public string FormattedSelectedItemUncompressedSize => SelectedItemType == "Directory" ? "-" : DataMetricsFormatter.FormatBytes(SelectedItemUncompressedSize);
    public string FormattedSelectedItemCompressedSize => (SelectedItemType == "Directory" || !SelectedItemCompressedSize.HasValue) ? "-" : DataMetricsFormatter.FormatBytes(SelectedItemCompressedSize.Value);
    public string FormattedSelectedItemRatio => SelectedItemType == "Directory" ? "-" : DataMetricsFormatter.FormatRatio(SelectedItemCompressedSize, SelectedItemUncompressedSize);
    public string SelectedItemLastModified { get; set; } = "-";
    public string SelectedItemPosixMode { get; set; } = "-";
    public string SelectedItemAttributes { get; set; } = "-";

    public event EventHandler? RequestClose;

    [RelayCommand]
    public void Close()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    public static async Task<ArchivePropertiesViewModel> CreateAsync(
        string archivePath,
        IArchiveEngine engine,
        ArchiveItemViewModel? selectedItem = null)
    {
        var fullPath = Path.GetFullPath(archivePath);
        var descriptor = ArchiveFormatRegistry.Detect(fullPath);
        var fileInfo = new FileInfo(fullPath);
        var compressedSize = fileInfo.Exists ? fileInfo.Length : 0;

        var entries = await engine.ListEntriesAsync(fullPath);

        int files = entries.Count(e => !e.IsDirectory);
        int dirs = entries.Count(e => e.IsDirectory);
        long uncompressed = entries.Where(e => !e.IsDirectory).Sum(e => e.UncompressedSize);

        var formatTitle = descriptor.Format switch
        {
            ArchiveFormat.Zrus => "Tar + Zstandard (.zrus / .tar.zstd)",
            ArchiveFormat.Zip => "Zip Archive (.zip)",
            ArchiveFormat.Rar => "RAR Archive (.rar)",
            ArchiveFormat.SevenZip => "7-Zip Archive (.7z)",
            ArchiveFormat.Zst => "Single-file Zstandard Stream (.zst)",
            ArchiveFormat.Gz => "Single-file GZip Stream (.gz)",
            ArchiveFormat.TarGz => "Tar + GZip Archive (.tar.gz)",
            _ => descriptor.Format.ToString()
        };

        var methodTitle = descriptor.Format switch
        {
            ArchiveFormat.Zrus or ArchiveFormat.Zst => "Zstandard (Block / Stream)",
            ArchiveFormat.Zip => "Deflate / Stored",
            ArchiveFormat.SevenZip => "LZMA / LZMA2",
            ArchiveFormat.Rar => "RAR Compression",
            ArchiveFormat.Gz or ArchiveFormat.TarGz => "DEFLATE (GZip)",
            _ => "Standard"
        };

        var vm = new ArchivePropertiesViewModel
        {
            ArchivePath = fullPath,
            ContainerFormat = formatTitle,
            CompressionMethod = methodTitle,
            TotalUncompressedSize = uncompressed,
            TotalCompressedSize = compressedSize,
            TotalFiles = files,
            TotalDirectories = dirs
        };

        if (selectedItem != null)
        {
            var matchingEntry = entries.FirstOrDefault(e => string.Equals(e.RelativePath, selectedItem.RelativePath, StringComparison.OrdinalIgnoreCase));
            var attrs = !string.IsNullOrEmpty(selectedItem.Attributes) ? selectedItem.Attributes : matchingEntry?.Attributes ?? "-";

            vm.SelectedItemName = selectedItem.Name;
            vm.SelectedItemRelativePath = selectedItem.RelativePath;
            vm.SelectedItemType = selectedItem.IsDirectory ? "Directory" : "File";
            vm.SelectedItemUncompressedSize = selectedItem.UncompressedSize;
            vm.SelectedItemCompressedSize = selectedItem.CompressedSize;
            vm.SelectedItemLastModified = selectedItem.FormattedLastModified;
            vm.SelectedItemPosixMode = attrs;
            vm.SelectedItemAttributes = attrs;
        }

        return vm;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;

namespace RusZip.Desktop.ViewModels;

public partial class FileConflictViewModel : ObservableObject
{
    public FileConflictContext Context { get; }

    public string FileName { get; }
    public string DirectoryPath { get; }
    public string RelativePath { get; }

    public string ExistingFileSizeFormatted { get; }
    public string ExistingLastModifiedFormatted { get; }

    public string IncomingFileSizeFormatted { get; }
    public string IncomingLastModifiedFormatted { get; }

    public Action<FileConflictResolution>? CloseWithResult { get; set; }

    public FileConflictViewModel(FileConflictContext context)
    {
        Context = context;
        FileName = Path.GetFileName(context.TargetPath);
        DirectoryPath = Path.GetDirectoryName(context.TargetPath) ?? string.Empty;
        RelativePath = context.RelativeEntryPath;

        ExistingFileSizeFormatted = DataMetricsFormatter.FormatBytes(context.ExistingFileSize);
        ExistingLastModifiedFormatted = context.ExistingLastModified.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

        IncomingFileSizeFormatted = DataMetricsFormatter.FormatBytes(context.EntryUncompressedSize);
        IncomingLastModifiedFormatted = context.EntryLastModified.HasValue
            ? context.EntryLastModified.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
            : "Unknown";
    }

    [RelayCommand]
    public void Overwrite() => CloseWithResult?.Invoke(FileConflictResolution.Overwrite);

    [RelayCommand]
    public void OverwriteAll() => CloseWithResult?.Invoke(FileConflictResolution.OverwriteAll);

    [RelayCommand]
    public void Skip() => CloseWithResult?.Invoke(FileConflictResolution.Skip);

    [RelayCommand]
    public void SkipAll() => CloseWithResult?.Invoke(FileConflictResolution.SkipAll);

    [RelayCommand]
    public void Abort() => CloseWithResult?.Invoke(FileConflictResolution.Abort);
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Core.Models;

namespace RusZip.Desktop.ViewModels;

public sealed partial class ArchiveTestResultViewModel : ObservableObject
{
    public ArchiveTestResult Result { get; }

    public bool IsSuccess => Result.IsSuccess;
    public bool HasErrors => Result.Errors.Count > 0;

    public string ArchiveFileName => Path.GetFileName(Result.ArchivePath);
    public string ArchivePath => Result.ArchivePath;
    public string Format => Result.Format.ToUpperInvariant();

    public string HeaderTitle => IsSuccess
        ? "Archive Integrity Verified"
        : "Archive Integrity Check Failed";

    public string StatusSummary => IsSuccess
        ? $"All {Result.TotalEntries} entries in '{ArchiveFileName}' passed integrity verification without corruption."
        : $"Integrity verification detected {Result.Errors.Count} error(s) in '{ArchiveFileName}'.";

    public string TotalEntriesText => $"{Result.TotalEntries:N0} entries";
    public string UncompressedBytesText => DataMetricsFormatter.FormatBytes(Result.UncompressedBytes);
    public string DurationText => Result.Duration.TotalSeconds < 1
        ? $"{Result.Duration.TotalMilliseconds:N0} ms"
        : $"{Result.Duration.TotalSeconds:N2} s";
    public string ThroughputText => $"{Result.ThroughputMBps:N2} MB/s";

    public IReadOnlyList<string> Errors => Result.Errors;

    public event EventHandler? RequestClose;

    public ArchiveTestResultViewModel(ArchiveTestResult result)
    {
        Result = result;
    }

    [RelayCommand]
    public void Close()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}

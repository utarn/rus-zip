namespace RusZip.Desktop.Services;

public interface IArchivePreviewService : IAsyncDisposable, IDisposable
{
    Task<string> ExtractPreviewAsync(string archivePath, string relativeEntryPath, CancellationToken ct = default);
    Task PreviewEntryAsync(string archivePath, string relativeEntryPath, CancellationToken ct = default);
    Task CleanupAsync();
    IReadOnlyCollection<string> ActivePreviewDirectories { get; }
}

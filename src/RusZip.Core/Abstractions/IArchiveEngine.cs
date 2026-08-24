using RusZip.Core.Models;

namespace RusZip.Core.Abstractions;

public interface IArchiveEngine
{
    Task CompressAsync(
        ArchiveCompressionRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default);

    Task<AppendResult> AppendAsync(
        ArchiveAppendRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default);

    Task<ArchiveDeleteResult> DeleteEntriesAsync(
        ArchiveDeleteRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default);

    Task<ExtractionResult> ExtractAsync(
        ArchiveExtractionRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default);

    Task<ArchiveTestResult> TestArchiveAsync(
        string archivePath,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default);

    Task<ArchiveTestResult> TestArchiveAsync(
        string archivePath,
        string? password,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        CancellationToken ct = default);

    Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        string? password,
        CancellationToken ct = default);

    Task<bool> IsEncryptedAsync(
        string archivePath,
        CancellationToken ct = default);
}

using RusZip.Core.Abstractions;
using RusZip.Core.Models;

namespace RusZip.Core.Engines;

public sealed class UnifiedArchiveEngine : IArchiveEngine
{
    private readonly ZstdTarArchiveEngine _zstdEngine;
    private readonly SharpCompressArchiveEngine _sharpCompressEngine;

    public UnifiedArchiveEngine()
    {
        _zstdEngine = new ZstdTarArchiveEngine();
        _sharpCompressEngine = new SharpCompressArchiveEngine();
    }

    public Task CompressAsync(
        ArchiveCompressionRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var format = ArchiveFormatDetector.DetectFromPath(request.DestinationArchivePath);
        return format switch
        {
            ArchiveFormat.Zrus => _zstdEngine.CompressAsync(request, progress, ct),
            ArchiveFormat.Zip => _sharpCompressEngine.CompressAsync(request, progress, ct),
            _ => throw new NotSupportedException($"Creation of '{format}' archive format is not supported. Supported creation formats: .zrus, .zip")
        };
    }

    public Task ExtractAsync(
        ArchiveExtractionRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var format = ArchiveFormatDetector.DetectFromPath(request.ArchivePath);
        return format switch
        {
            ArchiveFormat.Zrus => _zstdEngine.ExtractAsync(request, progress, ct),
            _ => _sharpCompressEngine.ExtractAsync(request, progress, ct)
        };
    }

    public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        CancellationToken ct = default)
    {
        var format = ArchiveFormatDetector.DetectFromPath(archivePath);
        return format switch
        {
            ArchiveFormat.Zrus => _zstdEngine.ListEntriesAsync(archivePath, ct),
            _ => _sharpCompressEngine.ListEntriesAsync(archivePath, ct)
        };
    }
}

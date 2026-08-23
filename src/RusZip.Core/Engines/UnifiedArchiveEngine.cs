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
        var descriptor = ArchiveFormatRegistry.Detect(request.DestinationArchivePath);
        if (!descriptor.CanCompress)
        {
            var supportedCreationFormats = string.Join(", ", ArchiveFormatRegistry.CompressibleFormats.Select(f => f.PrimaryExtension));
            throw new NotSupportedException($"Creation of '{descriptor.Format}' archive format is not supported. Supported creation formats: {supportedCreationFormats}");
        }

        return descriptor.Format switch
        {
            ArchiveFormat.Zrus => _zstdEngine.CompressAsync(request, progress, ct),
            ArchiveFormat.Zip => _sharpCompressEngine.CompressAsync(request, progress, ct),
            _ => throw new NotSupportedException($"Creation of '{descriptor.Format}' archive format is not supported. Supported creation formats: {string.Join(", ", ArchiveFormatRegistry.CompressibleFormats.Select(f => f.PrimaryExtension))}")
        };
    }

    public Task<AppendResult> AppendAsync(
        ArchiveAppendRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var descriptor = ArchiveFormatRegistry.Detect(request.ArchivePath);
        if (!descriptor.CanCompress)
        {
            throw new NotSupportedException($"Appending to '{descriptor.Format}' archive format is not supported.");
        }

        return descriptor.Format switch
        {
            ArchiveFormat.Zrus => _zstdEngine.AppendAsync(request, progress, ct),
            ArchiveFormat.Zip => _sharpCompressEngine.AppendAsync(request, progress, ct),
            _ => throw new NotSupportedException($"Appending to '{descriptor.Format}' archive format is not supported.")
        };
    }

    public Task<ExtractionResult> ExtractAsync(
        ArchiveExtractionRequest request,
        IProgress<ProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var descriptor = ArchiveFormatRegistry.Detect(request.ArchivePath);
        if (!descriptor.CanDecompress)
        {
            throw new NotSupportedException($"Extraction of '{descriptor.Format}' archive format is not supported.");
        }

        return descriptor.Format switch
        {
            ArchiveFormat.Zrus => _zstdEngine.ExtractAsync(request, progress, ct),
            _ => _sharpCompressEngine.ExtractAsync(request, progress, ct)
        };
    }

    public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        CancellationToken ct = default)
    {
        var descriptor = ArchiveFormatRegistry.Detect(archivePath);
        return descriptor.Format switch
        {
            ArchiveFormat.Zrus => _zstdEngine.ListEntriesAsync(archivePath, ct),
            _ => _sharpCompressEngine.ListEntriesAsync(archivePath, ct)
        };
    }
}

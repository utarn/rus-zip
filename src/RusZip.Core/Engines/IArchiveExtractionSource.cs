namespace RusZip.Core.Engines;

public sealed record ExtractionEntry(
    string RelativePath,
    bool IsDirectory,
    long UncompressedSize,
    DateTimeOffset? ModificationTime,
    UnixFileMode? UnixMode,
    Func<CancellationToken, ValueTask<Stream>> OpenStreamAsync
);

public interface IArchiveExtractionSource
{
    IAsyncEnumerable<ExtractionEntry> ReadEntriesAsync(CancellationToken ct = default);
}

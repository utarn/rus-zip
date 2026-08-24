namespace RusZip.Core.Models;

public sealed record ArchiveTestResult(
    bool IsSuccess,
    string ArchivePath,
    string Format,
    int TotalEntries,
    long UncompressedBytes,
    double ThroughputMBps,
    TimeSpan Duration,
    IReadOnlyList<string> Errors
);

namespace RusZip.Core.Models;

public sealed record ProgressReport(
    long ProcessedBytes,
    long TotalBytes,
    string? CurrentFileName,
    double Percentage,
    int ProcessedFiles = 0,
    int TotalFiles = 0,
    bool IsIndeterminate = false
);

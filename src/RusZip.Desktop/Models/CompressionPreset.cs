namespace RusZip.Desktop.Models;

public record CompressionPreset(
    int Level,
    string Name,
    string Ratio,
    string Throughput,
    string BadgeColor,
    string Description
);

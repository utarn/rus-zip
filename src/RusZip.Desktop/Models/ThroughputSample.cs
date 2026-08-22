namespace RusZip.Desktop.Models;

/// <summary>
/// A single throughput telemetry point backing the real-time velocity chart.
/// </summary>
/// <param name="Elapsed">Elapsed time since the current operation started.</param>
/// <param name="MegaBytesPerSec">Throughput in MiB/s (1 MiB = 1024 * 1024 bytes).</param>
public sealed record ThroughputSample(TimeSpan Elapsed, double MegaBytesPerSec);

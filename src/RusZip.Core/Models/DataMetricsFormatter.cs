using System.Diagnostics;
using System.Globalization;

namespace RusZip.Core.Models;

public static class DataMetricsFormatter
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string FormatBytes(long bytes, int decimalPlaces = 1)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";

        decimal number = bytes;
        int unitIndex = 0;

        while (number >= 1024m && unitIndex < ByteUnits.Length - 1)
        {
            number /= 1024m;
            unitIndex++;
        }

        var format = decimalPlaces > 0 ? $"N{decimalPlaces}" : "N0";
        return $"{number.ToString(format, CultureInfo.InvariantCulture)} {ByteUnits[unitIndex]}";
    }

    public static string FormatThroughput(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return "0 B/s";
        return $"{FormatBytes((long)bytesPerSec)}/s";
    }

    public static string FormatEta(TimeSpan eta)
    {
        if (eta.TotalSeconds <= 0) return "00:00";
        if (eta.TotalDays >= 1) return "> 24h";

        return eta.Hours > 0
            ? eta.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : eta.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    public static string FormatRatio(long? compressedBytes, long uncompressedBytes)
    {
        if (uncompressedBytes <= 0 || !compressedBytes.HasValue)
            return "-";

        double ratio = (double)compressedBytes.Value / uncompressedBytes * 100.0;
        return $"{ratio.ToString("0.0", CultureInfo.InvariantCulture)}%";
    }

    public static string FormatProgress(long processedBytes, long totalBytes)
    {
        var processed = FormatBytes(processedBytes);
        var total = totalBytes > 0 ? FormatBytes(totalBytes) : "...";
        return $"{processed} / {total}";
    }
}

public sealed class ThroughputTracker
{
    private readonly Stopwatch _stopwatch = new();
    private readonly double _alpha;
    private double _smoothedSpeed;
    private long _lastProcessedBytes;
    private double _lastTimestampSeconds;
    private bool _hasSample;

    public ThroughputTracker(double smoothingFactor = 0.3)
    {
        _alpha = Math.Clamp(smoothingFactor, 0.05, 0.95);
    }

    public void Start()
    {
        _stopwatch.Restart();
        _smoothedSpeed = 0;
        _lastProcessedBytes = 0;
        _lastTimestampSeconds = 0;
        _hasSample = false;
    }

    public void Reset()
    {
        _stopwatch.Reset();
        _smoothedSpeed = 0;
        _lastProcessedBytes = 0;
        _lastTimestampSeconds = 0;
        _hasSample = false;
    }

    /// <summary>
    /// Records a progress sample. Speed is measured as the delta since the previous
    /// <see cref="Update"/> call (<c>deltaBytes / deltaSeconds</c>) and EMA-smoothed, so the
    /// estimate reflects the current transfer rate rather than the cumulative average since
    /// <see cref="Start"/>. The first real sample seeds the smoothed speed directly.
    /// </summary>
    /// <param name="processedBytes">Cumulative bytes processed so far in the transfer.</param>
    /// <param name="totalBytes">Total bytes expected, used for ETA extrapolation only.</param>
    public void Update(long processedBytes, long totalBytes)
    {
        if (!_stopwatch.IsRunning)
        {
            _stopwatch.Start();
        }

        double currentTimestamp = _stopwatch.Elapsed.TotalSeconds;

        // Guard against a non-monotonic byte counter (e.g. a source reset mid-transfer).
        if (processedBytes < _lastProcessedBytes)
        {
            _lastProcessedBytes = processedBytes;
            _lastTimestampSeconds = currentTimestamp;
            return;
        }

        long deltaBytes = processedBytes - _lastProcessedBytes;
        double deltaSeconds = currentTimestamp - _lastTimestampSeconds;

        _lastProcessedBytes = processedBytes;
        _lastTimestampSeconds = currentTimestamp;

        // Zero-delta / zero-elapsed guard: a duplicate progress report (no new bytes, or two
        // samples in the same instant) must not collapse the smoothed speed to zero. Keep the
        // existing estimate and just refresh the baseline so the next real delta is clean.
        // The first sample also requires a minimum elapsed interval so a sub-millisecond early
        // report cannot seed an absurd spike.
        if (deltaBytes <= 0 || deltaSeconds <= 0 || (!_hasSample && deltaSeconds < 0.1))
        {
            return;
        }

        double currentInstantSpeed = deltaBytes / deltaSeconds;

        _smoothedSpeed = !_hasSample
            ? currentInstantSpeed
            : (_smoothedSpeed * (1.0 - _alpha)) + (currentInstantSpeed * _alpha);
        _hasSample = true;
    }

    public double SmoothedSpeedBytesPerSec => _smoothedSpeed;

    /// <summary>
    /// Extrapolates the remaining time from the remaining bytes and the smoothed speed.
    /// Returns <see langword="null"/> only when the total is unknown (indeterminate) or no
    /// speed sample has been recorded yet — never merely because the transfer is slow. A
    /// completed transfer returns <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining(long totalBytes)
    {
        if (totalBytes <= 0)
            return null;

        if (totalBytes <= _lastProcessedBytes)
            return TimeSpan.Zero;

        if (_smoothedSpeed <= 0)
            return null;

        long remainingBytes = totalBytes - _lastProcessedBytes;
        double secondsLeft = remainingBytes / _smoothedSpeed;
        return TimeSpan.FromSeconds(Math.Min(secondsLeft, 86400));
    }

    public string FormatSpeed() => DataMetricsFormatter.FormatThroughput(_smoothedSpeed);

    public string FormatEta(long totalBytes)
    {
        var eta = EstimatedTimeRemaining(totalBytes);
        return eta.HasValue ? DataMetricsFormatter.FormatEta(eta.Value) : "--:--";
    }

    public string FormatProgress(long totalBytes) =>
        DataMetricsFormatter.FormatProgress(_lastProcessedBytes, totalBytes);
}

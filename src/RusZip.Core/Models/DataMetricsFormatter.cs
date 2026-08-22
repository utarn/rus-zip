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

    public ThroughputTracker(double smoothingFactor = 0.3)
    {
        _alpha = Math.Clamp(smoothingFactor, 0.05, 0.95);
    }

    public void Start()
    {
        _stopwatch.Restart();
        _smoothedSpeed = 0;
        _lastProcessedBytes = 0;
    }

    public void Reset()
    {
        _stopwatch.Reset();
        _smoothedSpeed = 0;
        _lastProcessedBytes = 0;
    }

    public void Update(long processedBytes, long totalBytes)
    {
        if (!_stopwatch.IsRunning)
        {
            _stopwatch.Start();
        }

        _lastProcessedBytes = processedBytes;
        double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;

        if (elapsedSeconds > 0.1 && processedBytes > 0)
        {
            double currentInstantSpeed = processedBytes / elapsedSeconds;
            _smoothedSpeed = _smoothedSpeed == 0
                ? currentInstantSpeed
                : (_smoothedSpeed * (1.0 - _alpha)) + (currentInstantSpeed * _alpha);
        }
    }

    public double SmoothedSpeedBytesPerSec => _smoothedSpeed;

    public TimeSpan? EstimatedTimeRemaining(long totalBytes)
    {
        if (_smoothedSpeed <= 1024 || totalBytes <= _lastProcessedBytes || totalBytes <= 0)
        {
            return _lastProcessedBytes >= totalBytes && totalBytes > 0 ? TimeSpan.Zero : null;
        }

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

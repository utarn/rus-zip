using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

public class DataMetricsFormatterTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(-50L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048576L, "1.0 MB")]
    [InlineData(1073741824L, "1.0 GB")]
    [InlineData(1099511627776L, "1.0 TB")]
    [InlineData(1125899906842624L, "1.0 PB")]
    public void FormatBytes_FormatsCorrectly(long bytes, string expected)
    {
        var result = DataMetricsFormatter.FormatBytes(bytes);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatBytes_CustomDecimalPlaces()
    {
        Assert.Equal("1 KB", DataMetricsFormatter.FormatBytes(1024, decimalPlaces: 0));
        Assert.Equal("1.50 KB", DataMetricsFormatter.FormatBytes(1536, decimalPlaces: 2));
    }

    [Theory]
    [InlineData(0.0, "0 B/s")]
    [InlineData(-10.0, "0 B/s")]
    [InlineData(1024.0, "1.0 KB/s")]
    [InlineData(1048576.0, "1.0 MB/s")]
    [InlineData(52428800.0, "50.0 MB/s")]
    public void FormatThroughput_FormatsCorrectly(double bytesPerSec, string expected)
    {
        var result = DataMetricsFormatter.FormatThroughput(bytesPerSec);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatEta_FormatsCorrectly()
    {
        Assert.Equal("00:00", DataMetricsFormatter.FormatEta(TimeSpan.Zero));
        Assert.Equal("00:00", DataMetricsFormatter.FormatEta(TimeSpan.FromSeconds(-5)));
        Assert.Equal("00:45", DataMetricsFormatter.FormatEta(TimeSpan.FromSeconds(45)));
        Assert.Equal("02:30", DataMetricsFormatter.FormatEta(TimeSpan.FromMinutes(2.5)));
        Assert.Equal("01:15:30", DataMetricsFormatter.FormatEta(new TimeSpan(1, 15, 30)));
        Assert.Equal("> 24h", DataMetricsFormatter.FormatEta(TimeSpan.FromHours(25)));
    }

    [Fact]
    public void FormatRatio_FormatsCorrectly()
    {
        Assert.Equal("50.0%", DataMetricsFormatter.FormatRatio(500L, 1000L));
        Assert.Equal("33.3%", DataMetricsFormatter.FormatRatio(333L, 1000L));
        Assert.Equal("-", DataMetricsFormatter.FormatRatio(null, 1000L));
        Assert.Equal("-", DataMetricsFormatter.FormatRatio(100L, 0L));
        Assert.Equal("-", DataMetricsFormatter.FormatRatio(100L, -10L));
    }

    [Fact]
    public void FormatProgress_FormatsCorrectly()
    {
        Assert.Equal("1.0 MB / 10.0 MB", DataMetricsFormatter.FormatProgress(1048576L, 10485760L));
        Assert.Equal("1.0 MB / ...", DataMetricsFormatter.FormatProgress(1048576L, -1L));
        Assert.Equal("0 B / ...", DataMetricsFormatter.FormatProgress(0L, 0L));
    }

    [Fact]
    public async Task ThroughputTracker_TracksAndSmoothesSpeed()
    {
        var tracker = new ThroughputTracker(0.5);
        tracker.Start();

        await Task.Delay(150);
        tracker.Update(1048576L, 10485760L); // 1 MB of 10 MB

        Assert.True(tracker.SmoothedSpeedBytesPerSec > 0);
        Assert.NotNull(tracker.FormatSpeed());
        Assert.NotEqual("0 B/s", tracker.FormatSpeed());
        Assert.NotNull(tracker.FormatEta(10485760L));
        Assert.NotEqual("--:--", tracker.FormatEta(10485760L));
        Assert.Equal("1.0 MB / 10.0 MB", tracker.FormatProgress(10485760L));

        var eta = tracker.EstimatedTimeRemaining(10485760L);
        Assert.NotNull(eta);
        Assert.True(eta.Value.TotalSeconds > 0);
    }

    [Fact]
    public void ThroughputTracker_EstimatedTimeRemaining_WhenCompleted_ReturnsZero()
    {
        var tracker = new ThroughputTracker();
        tracker.Start();
        tracker.Update(1000L, 1000L);

        Assert.Equal(TimeSpan.Zero, tracker.EstimatedTimeRemaining(1000L));
        Assert.Equal("00:00", tracker.FormatEta(1000L));
    }

    [Fact]
    public void ThroughputTracker_EstimatedTimeRemaining_WhenIndeterminateOrZeroTotal_ReturnsNull()
    {
        var tracker = new ThroughputTracker();
        tracker.Start();
        tracker.Update(1000L, -1L);

        Assert.Null(tracker.EstimatedTimeRemaining(-1L));
        Assert.Equal("--:--", tracker.FormatEta(-1L));

        Assert.Null(tracker.EstimatedTimeRemaining(0L));
        Assert.Equal("--:--", tracker.FormatEta(0L));
    }

    [Fact]
    public async Task ThroughputTracker_Reset_ClearsState()
    {
        var tracker = new ThroughputTracker();
        tracker.Start();

        await Task.Delay(150);
        tracker.Update(1048576L, 10485760L);

        Assert.True(tracker.SmoothedSpeedBytesPerSec > 0);

        tracker.Reset();

        Assert.Equal(0, tracker.SmoothedSpeedBytesPerSec);
        Assert.Equal("0 B/s", tracker.FormatSpeed());
        Assert.Equal("0 B / ...", tracker.FormatProgress(0));
    }

    [Fact]
    public async Task ThroughputTracker_RespondsToMidTransferRateChange_MovesTowardNewRate()
    {
        var tracker = new ThroughputTracker(smoothingFactor: 0.5);
        tracker.Start();

        // First sample: 2 MB after ~200 ms ≈ 10 MB/s (seeds the smoothed speed).
        await Task.Delay(200);
        tracker.Update(2_000_000L, 10_000_000L);
        double fastSpeed = tracker.SmoothedSpeedBytesPerSec;
        Assert.True(fastSpeed > 5_000_000, $"expected a fast first sample, got {fastSpeed}");

        // Second sample adds only 200 KB after ~200 ms ≈ 1 MB/s. The EMA must move down
        // toward the new (slower) rate but stay above it (has not fully converged).
        await Task.Delay(200);
        tracker.Update(2_200_000L, 10_000_000L);
        double slowSpeed = tracker.SmoothedSpeedBytesPerSec;

        Assert.True(slowSpeed < fastSpeed, $"EMA should move down after a slowdown ({slowSpeed} >= {fastSpeed})");
        Assert.True(slowSpeed > 1_000_000, $"EMA should still sit above the new rate, got {slowSpeed}");
    }

    [Fact]
    public async Task ThroughputTracker_SubKilobyteSpeed_ReturnsEtaInsteadOfBlank()
    {
        var tracker = new ThroughputTracker(smoothingFactor: 0.5);
        tracker.Start();

        // ~100 bytes over >=200 ms stays below the old 1 KB/s dead-zone.
        await Task.Delay(200);
        tracker.Update(100L, 1_000_000L);

        Assert.True(tracker.SmoothedSpeedBytesPerSec > 0);
        Assert.True(tracker.SmoothedSpeedBytesPerSec < 1024,
            $"expected sub-1KB/s smoothed speed, got {tracker.SmoothedSpeedBytesPerSec}");

        var eta = tracker.EstimatedTimeRemaining(1_000_000L);
        Assert.NotNull(eta);
        Assert.True(eta.Value.TotalSeconds > 0);
        Assert.NotEqual("--:--", tracker.FormatEta(1_000_000L));
    }

    [Fact]
    public async Task ThroughputTracker_ZeroDeltaUpdate_PreservesSmoothedSpeed()
    {
        var tracker = new ThroughputTracker(smoothingFactor: 0.5);
        tracker.Start();

        await Task.Delay(200);
        tracker.Update(1_000_000L, 10_000_000L);
        double speedBefore = tracker.SmoothedSpeedBytesPerSec;
        Assert.True(speedBefore > 0);

        // A duplicate progress report with no new bytes must not collapse the estimate to zero.
        tracker.Update(1_000_000L, 10_000_000L);
        Assert.Equal(speedBefore, tracker.SmoothedSpeedBytesPerSec);

        // A subsequent real delta still updates the estimate from the refreshed baseline.
        await Task.Delay(200);
        tracker.Update(1_200_000L, 10_000_000L);
        Assert.NotEqual(speedBefore, tracker.SmoothedSpeedBytesPerSec);
    }
}

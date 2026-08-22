using ProCharts;

namespace RusZip.Desktop.ViewModels;

/// <summary>
/// Adapts the VM's rolling throughput series to ProCharts' <see cref="IChartDataSource"/>
/// contract. The buffer caps at a few hundred points, so rebuilding a full snapshot on
/// every change (a few times a second) is trivially cheap and avoids incremental delta bookkeeping.
/// </summary>
public sealed class ThroughputChartDataSource : IChartDataSource
{
    private readonly ThroughputSeriesBuffer _buffer;

    /// <summary>Initializes a data source that reads from the supplied rolling buffer.</summary>
    public ThroughputChartDataSource(ThroughputSeriesBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _buffer.SamplesChanged += OnSamplesChanged;
    }

    /// <inheritdoc />
    public event EventHandler? DataInvalidated;

    /// <inheritdoc />
    public ChartDataSnapshot BuildSnapshot(ChartDataRequest request)
    {
        var samples = _buffer.Samples;
        var categories = new string?[samples.Count];
        var values = new double?[samples.Count];

        for (int i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            categories[i] = FormatElapsed(sample.Elapsed);
            values[i] = sample.MegaBytesPerSec;
        }

        var series = new ChartSeriesSnapshot(
            name: "Throughput",
            kind: ChartSeriesKind.Area,
            values: values,
            style: new ChartSeriesStyle
            {
                StrokeColor = ChartColor.FromRgb(0x2E, 0x8B, 0xF0),
                StrokeWidth = 2,
                FillColor = ChartColor.FromArgb(0x3D, 0x2E, 0x8B, 0xF0),
                LineInterpolation = ChartLineInterpolation.Smooth,
                MarkerShape = ChartMarkerShape.None
            });

        return new ChartDataSnapshot(categories, new[] { series }, _buffer.Count);
    }

    private void OnSamplesChanged(object? sender, EventArgs e)
    {
        DataInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        int totalSeconds = (int)elapsed.TotalSeconds;
        return $"{totalSeconds / 60:0}:{totalSeconds % 60:00}";
    }
}

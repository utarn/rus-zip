using System.Collections.ObjectModel;
using RusZip.Desktop.Models;

namespace RusZip.Desktop.ViewModels;

/// <summary>
/// Rolling time-series buffer of throughput samples backing the velocity chart.
/// Samples are appended in elapsed-time order and trimmed from the front so the
/// series never exceeds a fixed wall-clock window (60 seconds by default) nor a
/// hard capacity cap. Mutations raise <see cref="SamplesChanged"/> exactly once
/// per change so consumers (the chart data source) can refresh without reacting
/// to intermediate trim steps.
/// </summary>
public sealed class ThroughputSeriesBuffer
{
    private readonly TimeSpan _window;
    private readonly int _maxCapacity;

    /// <summary>
    /// Initializes a new rolling buffer.
    /// </summary>
    /// <param name="window">Rolling wall-clock window; non-positive values fall back to 60 seconds.</param>
    /// <param name="maxCapacity">Hard point cap; non-positive values fall back to 600.</param>
    public ThroughputSeriesBuffer(TimeSpan window, int maxCapacity)
    {
        _window = window > TimeSpan.Zero ? window : TimeSpan.FromSeconds(60);
        _maxCapacity = maxCapacity > 0 ? maxCapacity : 600;
    }

    /// <summary>Gets the live sample collection (read-only consumers should use <see cref="IReadOnlyList{T}"/> semantics).</summary>
    public ObservableCollection<ThroughputSample> Samples { get; } = new();

    /// <summary>Gets the number of samples currently buffered.</summary>
    public int Count => Samples.Count;

    /// <summary>Gets the rolling window this buffer preserves.</summary>
    public TimeSpan Window => _window;

    /// <summary>Gets the hard capacity cap.</summary>
    public int MaxCapacity => _maxCapacity;

    /// <summary>
    /// Raised once after every mutation (<see cref="Add"/> or <see cref="Clear"/>)
    /// that actually changed the buffer contents.
    /// </summary>
    public event EventHandler? SamplesChanged;

    /// <summary>
    /// Appends a sample and trims the buffer to the rolling window and capacity.
    /// The <paramref name="elapsed"/> values are expected to be monotonically
    /// non-decreasing (they come from the operation's stopwatch).
    /// </summary>
    public void Add(TimeSpan elapsed, double megaBytesPerSec)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        if (double.IsNaN(megaBytesPerSec) || double.IsInfinity(megaBytesPerSec) || megaBytesPerSec < 0)
            megaBytesPerSec = 0;

        Samples.Add(new ThroughputSample(elapsed, megaBytesPerSec));
        TrimToCapacity();

        // Trim to the rolling window relative to the newest sample.
        TimeSpan cutoff = Samples[^1].Elapsed - _window;
        while (Samples.Count > 1 && Samples[0].Elapsed < cutoff)
            Samples.RemoveAt(0);

        SamplesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes all buffered samples.</summary>
    public void Clear()
    {
        if (Samples.Count == 0)
            return;
        Samples.Clear();
        SamplesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TrimToCapacity()
    {
        while (Samples.Count > _maxCapacity)
            Samples.RemoveAt(0);
    }
}

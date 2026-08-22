using CommunityToolkit.Mvvm.ComponentModel;
using RusZip.Core.Engines;
using RusZip.Core.Models;

namespace RusZip.Desktop.ViewModels;

/// <summary>
/// Small extraction-guardrail settings surface (ADR-0007). Mirrors the CLI's
/// <c>--max-uncompressed-size</c> and <c>--max-entries</c> flags; a value of zero (or an
/// unparseable size) means "unlimited". Sensible defaults match <see cref="SafeArchiveExtractor"/>.
/// </summary>
public partial class ExtractionSettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _maxUncompressedSizeText =
        DataMetricsFormatter.FormatBytes(SafeArchiveExtractor.DefaultMaxCumulativeUncompressedBytes);

    [ObservableProperty] private decimal? _maxEntryCount = SafeArchiveExtractor.DefaultMaxEntryCount;

    /// <summary>
    /// Builds the <see cref="ExtractionLimits"/> applied to the next extraction request.
    /// A non-positive or unparseable size/entry count maps to <see langword="null"/> (unlimited).
    /// </summary>
    public ExtractionLimits BuildLimits()
    {
        long? maxBytes = null;
        if (DataSizeParser.TryParse(MaxUncompressedSizeText, out var parsedBytes) && parsedBytes > 0)
        {
            maxBytes = parsedBytes;
        }

        int? maxEntries = null;
        if (MaxEntryCount.HasValue && MaxEntryCount.Value > 0)
        {
            maxEntries = (int)Math.Min(MaxEntryCount.Value, int.MaxValue);
        }

        return new ExtractionLimits(maxBytes, maxEntries);
    }
}

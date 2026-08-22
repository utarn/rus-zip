using System.Globalization;
using System.Text.RegularExpressions;

namespace RusZip.Core.Models;

/// <summary>
/// Parses byte sizes expressed either as a plain byte count ("12345678") or with a human
/// unit suffix ("10GB", "1.5MB", "512KiB"). Powers of 1024 are used for KB/MB/GB/TB and the
/// KiB/MiB/GiB/TiB binary aliases; matching is case-insensitive and whitespace between the
/// number and unit is tolerated.
/// </summary>
public static partial class DataSizeParser
{
    [GeneratedRegex(@"^\s*([0-9]*\.?[0-9]+)\s*([a-zA-Z]*)\s*$", RegexOptions.Compiled)]
    private static partial Regex SizePattern();

    /// <summary>
    /// Attempts to parse <paramref name="input"/> into a byte count. Returns <see langword="false"/>
    /// (and 0) when the input is not a valid size expression.
    /// </summary>
    public static bool TryParse(string? input, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var match = SizePattern().Match(input);
        if (!match.Success)
            return false;

        if (!decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            return false;

        var unit = match.Groups[2].Value.ToLowerInvariant();
        decimal multiplier = unit switch
        {
            "" or "b" => 1m,
            "kb" or "kib" => 1024m,
            "mb" or "mib" => 1024m * 1024m,
            "gb" or "gib" => 1024m * 1024m * 1024m,
            "tb" or "tib" => 1024m * 1024m * 1024m * 1024m,
            _ => 0m
        };

        if (multiplier == 0m)
            return false;

        try
        {
            var total = checked((long)(number * multiplier));
            bytes = total;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}

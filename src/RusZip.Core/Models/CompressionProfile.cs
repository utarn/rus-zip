namespace RusZip.Core.Models;

/// <summary>
/// Standard preset compression profiles mapping to Zstandard compression levels.
/// </summary>
public enum CompressionProfile
{
    /// <summary>
    /// Level 3: High speed compression, lower CPU overhead.
    /// </summary>
    Fast = 3,

    /// <summary>
    /// Level 9: Balanced general-purpose compression (default).
    /// </summary>
    Balanced = 9,

    /// <summary>
    /// Level 15: High compression ratio for distribution.
    /// </summary>
    High = 15,

    /// <summary>
    /// Level 22: Ultra / maximum Zstandard compression ratio.
    /// </summary>
    Ultra = 22
}

/// <summary>
/// Helper utilities and constants for compression profiles and levels.
/// </summary>
public static class CompressionProfiles
{
    public const int MinLevel = 1;
    public const int MaxLevel = 22;
    public const int DefaultLevel = 9;

    public const int Fast = 3;
    public const int Balanced = 9;
    public const int High = 15;
    public const int Ultra = 22;

    /// <summary>
    /// Resolves compression level from profile name and/or explicit level.
    /// </summary>
    public static int ResolveLevel(string? profileName, int? level = null)
    {
        if (level.HasValue)
        {
            return level.Value;
        }

        return profileName?.Trim().ToLowerInvariant() switch
        {
            "fast" => Fast,
            "balanced" => Balanced,
            "high" => High,
            "ultra" => Ultra,
            _ => DefaultLevel
        };
    }

    /// <summary>
    /// Maps an integer compression level to the nearest compression profile category.
    /// </summary>
    public static CompressionProfile FromLevel(int level) => level switch
    {
        <= 5 => CompressionProfile.Fast,
        <= 11 => CompressionProfile.Balanced,
        <= 18 => CompressionProfile.High,
        _ => CompressionProfile.Ultra
    };
}

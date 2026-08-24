namespace RusZip.Desktop;

/// <summary>
/// Centralized display branding constants and title formatters across all user-visible surfaces.
/// </summary>
public static class AppBranding
{
    /// <summary>
    /// Canonical display name for the application.
    /// </summary>
    public const string DisplayName = "RUS ZIP";

    /// <summary>
    /// Main window title.
    /// </summary>
    public const string MainWindowTitle = "RUS ZIP - Compression Suite";

    /// <summary>
    /// Quick extract window title.
    /// </summary>
    public const string QuickExtractWindowTitle = "Quick Extract - RUS ZIP";

    /// <summary>
    /// About dialog window title.
    /// </summary>
    public const string AboutDialogTitle = "About RUS ZIP";

    /// <summary>
    /// Native menu header for About.
    /// </summary>
    public const string AboutMenuHeader = "About RUS ZIP";

    /// <summary>
    /// In-window menu header for About.
    /// </summary>
    public const string AboutInWindowMenuHeader = "_About RUS ZIP";

    /// <summary>
    /// Formats a dialog title to end with "- RUS ZIP".
    /// </summary>
    public static string FormatDialogTitle(string featureName) => $"{featureName} - {DisplayName}";
}

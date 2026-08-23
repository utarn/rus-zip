namespace RusZip.Desktop.Models;

/// <summary>
/// Mode of quick extraction triggered via CLI or context-menu arguments.
/// </summary>
public enum QuickExtractMode
{
    /// <summary>
    /// Extract directly to the archive's parent directory with interactive conflict resolution.
    /// </summary>
    ExtractHere,

    /// <summary>
    /// Extract to a specified or user-prompted directory with interactive conflict resolution.
    /// </summary>
    ExtractTo,

    /// <summary>
    /// Extract into an auto-suffixed dedicated subfolder without prompting for file conflicts.
    /// </summary>
    ExtractToDir
}

/// <summary>
/// Encapsulates options for a standalone quick-extraction session.
/// </summary>
public sealed record QuickExtractOptions(
    QuickExtractMode Mode,
    string ArchivePath,
    string? DestinationDirectory = null
);

/// <summary>
/// Parser for CLI extraction handler flags (--extract-here, --extract-to, --extract-to-dir).
/// </summary>
public static class QuickExtractCommandLineParser
{
    /// <summary>
    /// Parses CLI arguments to detect quick extract requests.
    /// </summary>
    /// <param name="args">Command-line argument array.</param>
    /// <returns>A <see cref="QuickExtractOptions"/> if matching flags were provided; otherwise <c>null</c>.</returns>
    public static QuickExtractOptions? Parse(string[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (arg.Equals("--extract-here", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--extract-here=", StringComparison.OrdinalIgnoreCase))
            {
                string? path = ExtractValue(arg, args, ref i);
                if (!string.IsNullOrEmpty(path))
                {
                    return new QuickExtractOptions(QuickExtractMode.ExtractHere, path);
                }
            }
            else if (arg.Equals("--extract-to-dir", StringComparison.OrdinalIgnoreCase) ||
                     arg.StartsWith("--extract-to-dir=", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("--extract-to-subfolder", StringComparison.OrdinalIgnoreCase) ||
                     arg.StartsWith("--extract-to-subfolder=", StringComparison.OrdinalIgnoreCase))
            {
                string? path = ExtractValue(arg, args, ref i);
                if (!string.IsNullOrEmpty(path))
                {
                    return new QuickExtractOptions(QuickExtractMode.ExtractToDir, path);
                }
            }
            else if (arg.Equals("--extract-to", StringComparison.OrdinalIgnoreCase) ||
                     arg.StartsWith("--extract-to=", StringComparison.OrdinalIgnoreCase))
            {
                string? path = ExtractValue(arg, args, ref i);
                string? destination = null;

                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    destination = args[++i].Trim('"', '\'');
                }

                if (!string.IsNullOrEmpty(path))
                {
                    return new QuickExtractOptions(QuickExtractMode.ExtractTo, path, destination);
                }
            }
        }

        return null;
    }

    private static string? ExtractValue(string currentArg, string[] args, ref int index)
    {
        var eqIndex = currentArg.IndexOf('=');
        if (eqIndex >= 0)
        {
            return currentArg[(eqIndex + 1)..].Trim('"', '\'');
        }

        if (index + 1 < args.Length && !args[index + 1].StartsWith('-'))
        {
            index++;
            return args[index].Trim('"', '\'');
        }

        return null;
    }
}

using Avalonia;
using RusZip.Desktop.Models;
using RusZip.Desktop.Services;

namespace RusZip.Desktop;

public static class Program
{
    private static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--theme", "-t",
        "--profile", "-p",
        "--config", "-c",
        "--output", "-o"
    };

    [STAThread]
    public static int Main(string[] args)
    {
        // CLI quick extraction flags (--extract-here, --extract-to, --extract-to-dir) bypass single-instance IPC
        if (QuickExtractCommandLineParser.Parse(args) == null)
        {
            var coordinator = new SingleInstanceCoordinator();
            string? filePath = ExtractArchiveArgument(args);
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                if (coordinator.TrySendToExistingInstanceAsync(filePath, cts.Token).GetAwaiter().GetResult())
                {
                    // Successfully forwarded request to primary instance; terminate secondary instance immediately with exit code 0.
                    return 0;
                }
            }
            catch
            {
                // Fallback to running as primary instance if IPC connection check fails
            }
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Extracts the target archive file path from command-line arguments, skipping flags and known options.
    /// </summary>
    /// <param name="args">The argument array.</param>
    /// <returns>The first non-option argument string representing an archive path, or null.</returns>
    public static string? ExtractArchiveArgument(string[]? args)
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

            if (arg.StartsWith('-'))
            {
                if (ValueOptions.Contains(arg) && i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    i++; // Skip the option's value
                }
                continue;
            }

            return arg.Trim('"', '\'');
        }

        return null;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

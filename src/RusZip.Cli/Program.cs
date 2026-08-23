using Microsoft.Extensions.DependencyInjection;
using RusZip.Cli.Commands;
using RusZip.Cli.Infrastructure;
using RusZip.Core.Abstractions;
using RusZip.Core.Engines;
using Spectre.Console;
using Spectre.Console.Cli;

namespace RusZip.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await RunWithConsoleAsync(args, AnsiConsole.Console);
    }

    public static async Task<int> RunWithConsoleAsync(string[] args, IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        AnsiConsole.Console = console;

        // Clear interceptor state from any prior invocation so a parse error in this run cannot
        // pick up a stale JSON flag bound by an earlier one.
        JsonModeInterceptor.Reset();

        var services = new ServiceCollection();
        services.AddSingleton<IArchiveEngine, UnifiedArchiveEngine>();

        var registrar = new TypeRegistrar(services);
        var app = new CommandApp(registrar);

        // F-22: normalize argv before handing it to Spectre so a literal file named "--json"
        // (or any other option-shaped value) can be referenced after a "--" separator. Everything
        // after "--" is treated as positional and prefixed with "./" when it looks like an option,
        // which makes Spectre bind it as a value rather than a flag. As a side effect, after
        // normalization a bare "--json"/"-j" token can only ever mean the JSON flag — so the
        // exception handler's pre-binding fallback never misfires on a literal filename.
        var normalizedArgs = NormalizeArgs(args);
        var effectiveArgs = normalizedArgs.Length == 0 ? ["--help"] : normalizedArgs;

        app.Configure(config =>
        {
            config.ConfigureConsole(console);
            config.SetApplicationName("rus-zip");
            config.SetApplicationVersion("1.0.0");

            config.SetHelpProvider(new AiHelpProvider(config.Settings));

            config.AddCommand<CompressCommand>("compress")
                .WithAlias("c")
                .WithDescription("Compress files or directories into .zrus or .zip archives.")
                .WithExample(["docs/", "backup.zrus", "-p", "high"])
                .WithExample(["file.txt", "archive.zip", "-l", "9", "--json"])
                .WithExample(["photos/", "album.zrus", "--profile", "ultra"]);

            config.AddCommand<AppendCommand>("append")
                .WithAlias("a")
                .WithAlias("add")
                .WithDescription("Append files or directories to an existing .zrus archive.")
                .WithExample(["backup.zrus", "newfile.txt"])
                .WithExample(["backup.zrus", "photos/", "-u"])
                .WithExample(["archive.zrus", "docs/", "--update-only", "--json"]);

            config.AddCommand<ExtractCommand>("extract")
                .WithAlias("x")
                .WithDescription("Extract an archive (.zrus, .zip, .rar, .7z, .gz, .tar.gz) to a directory.")
                .WithExample(["backup.zrus", "-o", "./output"])
                .WithExample(["data.7z", "-o", "./extracted", "--json"]);

            config.AddCommand<ListCommand>("list")
                .WithAlias("l")
                .WithDescription("List files and directories inside an archive without extracting.")
                .WithExample(["archive.zrus"])
                .WithExample(["package.zip", "--json"]);

            // Record the JSON/verbose-errors flags from the actual parsed settings binding so the
            // global handler below does not have to guess from raw argv (F-22).
            config.SetInterceptor(new JsonModeInterceptor());
            config.SetExceptionHandler((ex, resolver) =>
            {
                bool isJson;
                bool verboseErrors;
                if (JsonModeInterceptor.LastInvocationHadSettingsBound)
                {
                    isJson = JsonModeInterceptor.LastBoundJson;
                    verboseErrors = JsonModeInterceptor.LastBoundVerboseErrors;
                }
                else
                {
                    isJson = normalizedArgs.Any(a => a is "--json" or "-j");
                    verboseErrors = normalizedArgs.Any(a => a == "--verbose-errors");
                }

                return CliCommandRunner.HandleException(ex, isJson, verboseErrors: verboseErrors);
            });
        });

        // If typing the cli executable file without any parameters, show --help
        return await app.RunAsync(effectiveArgs);
    }

    /// <summary>
    /// Implements the standard <c>--</c> end-of-options separator (Spectre 0.49 has no native
    /// support for it). The separator token itself is dropped and every following token that
    /// looks like an option is prefixed with <c>./</c> so Spectre binds it as a positional value
    /// instead of a flag.
    /// </summary>
    internal static string[] NormalizeArgs(string[] args)
    {
        var result = new List<string>(args.Length);
        bool afterSeparator = false;
        foreach (var arg in args)
        {
            if (!afterSeparator && arg == "--")
            {
                afterSeparator = true;
                continue;
            }

            result.Add(afterSeparator && LooksLikeOption(arg) ? "./" + arg : arg);
        }

        return [.. result];
    }

    private static bool LooksLikeOption(string arg) =>
        arg.Length > 1
        && arg[0] == '-'
        && !arg.StartsWith("./")
        && !arg.StartsWith("../")
        && !arg.Contains('/')
        && !arg.Contains('\\');
}

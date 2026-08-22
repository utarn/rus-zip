using System.Security;
using Microsoft.Extensions.DependencyInjection;
using RusZip.Cli.Commands;
using RusZip.Cli.Infrastructure;
using RusZip.Cli.Models;
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

        var services = new ServiceCollection();
        services.AddSingleton<IArchiveEngine, UnifiedArchiveEngine>();

        var registrar = new TypeRegistrar(services);
        var app = new CommandApp(registrar);

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

            config.SetExceptionHandler((ex, resolver) =>
            {
                bool isJson = args.Any(a => a is "--json" or "-j");

                if (ex is CommandParseException)
                {
                    if (isJson)
                    {
                        CliJsonSerializer.EmitError("ARGUMENT_ERROR", ex.Message);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                    }
                    return 2;
                }

                var actual = ex is CommandRuntimeException runtimeEx && runtimeEx.InnerException != null
                    ? runtimeEx.InnerException
                    : ex;

                if (actual is FileNotFoundException or DirectoryNotFoundException)
                {
                    if (isJson)
                    {
                        CliJsonSerializer.EmitError("SOURCE_NOT_FOUND", actual.Message);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(actual.Message)}");
                    }
                    return 2;
                }

                if (actual is SecurityException sec)
                {
                    if (isJson)
                    {
                        CliJsonSerializer.EmitError("SECURITY_VIOLATION", sec.Message);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Security Violation:[/] {Markup.Escape(sec.Message)}");
                    }
                    return 1;
                }

                if (actual is NotSupportedException nse)
                {
                    if (isJson)
                    {
                        CliJsonSerializer.EmitError("UNSUPPORTED_FORMAT", nse.Message);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(nse.Message)}");
                    }
                    return 2;
                }

                if (actual is CommandAppException appEx)
                {
                    if (isJson)
                    {
                        CliJsonSerializer.EmitError("ARGUMENT_ERROR", appEx.Message);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(appEx.Message)}");
                    }
                    return 2;
                }

                if (isJson)
                {
                    CliJsonSerializer.EmitError("EXECUTION_ERROR", actual.Message, actual.StackTrace);
                }
                else
                {
                    AnsiConsole.WriteException(actual);
                }
                return 1;
            });
        });

        // If typing the cli executable file without any parameters, show --help
        string[] effectiveArgs = args.Length == 0 ? ["--help"] : args;

        return await app.RunAsync(effectiveArgs);
    }
}

using Microsoft.Extensions.DependencyInjection;
using RusZip.Cli.Commands;
using RusZip.Cli.Infrastructure;
using RusZip.Core.Abstractions;
using RusZip.Core.Engines;
using Spectre.Console.Cli;

namespace RusZip.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IArchiveEngine, UnifiedArchiveEngine>();

        var registrar = new TypeRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("rus-zip");
            config.SetApplicationVersion("1.0.0");
            config.ValidateExamples();

            config.SetHelpProvider(new AiHelpProvider(config.Settings));

            config.AddCommand<CompressCommand>("compress")
                .WithAlias("c")
                .WithDescription("Compress files or directories into .zrus or .zip archives.")
                .WithExample(["compress", "docs/", "backup.zrus", "-p", "high"])
                .WithExample(["compress", "file.txt", "archive.zip", "-l", "9", "--json"])
                .WithExample(["compress", "photos/", "album.zrus", "--profile", "ultra"]);

            config.AddCommand<ExtractCommand>("extract")
                .WithAlias("x")
                .WithDescription("Extract an archive (.zrus, .zip, .rar, .7z, .gz, .tar.gz) to a directory.")
                .WithExample(["extract", "backup.zrus", "-o", "./output"])
                .WithExample(["extract", "data.7z", "-o", "./extracted", "--json"]);

            config.AddCommand<ListCommand>("list")
                .WithAlias("l")
                .WithDescription("List files and directories inside an archive without extracting.")
                .WithExample(["list", "archive.zrus"])
                .WithExample(["list", "package.zip", "--json"]);
        });

        // If typing the cli executable file without any parameters, show --help
        string[] effectiveArgs = args.Length == 0 ? ["--help"] : args;

        return await app.RunAsync(effectiveArgs);
    }
}

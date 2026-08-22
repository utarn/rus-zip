using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using Spectre.Console.Rendering;

namespace RusZip.Cli.Infrastructure;

public sealed class AiHelpProvider(ICommandAppSettings settings) : HelpProvider(settings)
{
    public override IEnumerable<IRenderable> GetHeader(ICommandModel model, ICommandInfo? command)
    {
        if (command == null)
        {
            yield return new FigletText("rus-zip")
                .LeftJustified()
                .Color(Color.Cyan1);

            yield return new Markup("[bold white]rus-zip[/] - Cross-platform archive suite powered by Tar+Zstandard (.zrus) and SharpCompress\n");
            yield return new Markup("[dim]Optimized for human terminals and AI agents.[/]\n\n");
        }
        else
        {
            foreach (var item in base.GetHeader(model, command))
            {
                yield return item;
            }
        }
    }

    public override IEnumerable<IRenderable> GetFooter(ICommandModel model, ICommandInfo? command)
    {
        if (command == null)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey35)
                .AddColumn(new TableColumn("[bold cyan]Profile[/]"))
                .AddColumn(new TableColumn("[bold cyan]Level[/]"))
                .AddColumn(new TableColumn("[bold cyan]Description[/]"))
                .AddRow("[green]fast[/]", "3", "High-speed streaming compression, minimal CPU usage")
                .AddRow("[yellow]balanced[/]", "9", "Default profile, optimal speed/ratio balance")
                .AddRow("[blue]high[/]", "15", "Improved ratio for distribution")
                .AddRow("[red]ultra[/]", "22", "Maximum Zstandard compression ratio");

            yield return new Markup("[bold yellow]COMPRESSION PROFILES (.zrus):[/]\n");
            yield return table;

            yield return new Markup("\n[bold yellow]SUPPORTED FORMATS:[/]\n" +
                                    "  • [green]Compress & Decompress:[/] .zrus (Tar+Zstd), .zip\n" +
                                    "  • [cyan]Decompress Only:[/]        .rar, .7z, .gz, .tar.gz\n\n");

            yield return new Markup("[bold yellow]EXIT CODES:[/]\n" +
                                    "  [green]0[/] = Success\n" +
                                    "  [red]1[/] = Execution / Engine error\n" +
                                    "  [red]2[/] = Invalid arguments / Path not found\n\n");

            yield return new Markup("[dim]Machine-readable output available via [bold]--json[/] on all commands.[/]\n");
        }
        else
        {
            yield return new Markup($"\n[dim]Run [bold]rus-zip --help[/] for global commands, compression profiles, and format matrix.[/]\n");
        }
    }
}

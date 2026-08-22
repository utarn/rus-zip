using System.ComponentModel;
using Spectre.Console.Cli;

namespace RusZip.Cli.Commands.Settings;

public abstract class JsonCommandSettings : CommandSettings
{
    [CommandOption("-j|--json")]
    [Description("Output response in machine-readable JSON format.")]
    [DefaultValue(false)]
    public bool Json { get; init; }
}

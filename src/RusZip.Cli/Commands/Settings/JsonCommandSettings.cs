using System.ComponentModel;
using Spectre.Console.Cli;

namespace RusZip.Cli.Commands.Settings;

public abstract class JsonCommandSettings : CommandSettings
{
    [CommandOption("-j|--json")]
    [Description("Output response in machine-readable JSON format.")]
    [DefaultValue(false)]
    public bool Json { get; init; }

    [CommandOption("--verbose-errors")]
    [Description("Include full exception stack traces in --json error output. Off by default; enable only when diagnosing failures.")]
    [DefaultValue(false)]
    public bool VerboseErrors { get; init; }
}

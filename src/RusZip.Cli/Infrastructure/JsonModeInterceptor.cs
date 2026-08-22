using RusZip.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace RusZip.Cli.Infrastructure;

/// <summary>
/// Captures the parsed <c>--json</c> / <c>--verbose-errors</c> flags from the actual settings
/// binding (F-22). The global exception handler must not guess JSON mode by scanning raw argv —
/// a file literally named <c>--json</c> referenced after a <c>--</c> separator (or as
/// <c>./--json</c>) would otherwise force JSON error output for a non-JSON invocation. When
/// settings bind successfully, this interceptor records the authoritative flag values; for
/// pre-binding parse errors (settings never bind) the caller falls back to the normalized
/// argument list, where a bare <c>--json</c>/<c>-j</c> token can only ever be the flag.
/// </summary>
public sealed class JsonModeInterceptor : ICommandInterceptor
{
    public static bool LastInvocationHadSettingsBound { get; private set; }
    public static bool LastBoundJson { get; private set; }
    public static bool LastBoundVerboseErrors { get; private set; }

    public void Intercept(CommandContext context, CommandSettings settings)
    {
        LastInvocationHadSettingsBound = true;
        if (settings is JsonCommandSettings json)
        {
            LastBoundJson = json.Json;
            LastBoundVerboseErrors = json.VerboseErrors;
        }
        else
        {
            LastBoundJson = false;
            LastBoundVerboseErrors = false;
        }
    }

    public void InterceptResult(CommandContext context, CommandSettings settings, ref int result)
    {
        // No post-execution work needed; the interceptor exists to observe settings binding.
    }

    public static void Reset()
    {
        LastInvocationHadSettingsBound = false;
        LastBoundJson = false;
        LastBoundVerboseErrors = false;
    }
}

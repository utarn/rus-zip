using System.Diagnostics;
using System.Security;
using RusZip.Cli.Models;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace RusZip.Cli.Infrastructure;

public static class CliCommandRunner
{
    public static async Task<int> RunAsync<TResult>(
        string operationTitle,
        bool isJson,
        Func<IProgress<ProgressReport>?, CancellationToken, Task<TResult>> operation,
        Action<TResult, long>? renderConsoleSummary = null,
        CancellationToken ct = default,
        TextWriter? outputWriter = null,
        bool verboseErrors = false)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            TResult? result = default;

            await CliProgressBridge.ExecuteWithProgressAsync(
                operationTitle,
                isJson,
                async (progress, token) =>
                {
                    result = await operation(progress, token);
                },
                ct
            );

            sw.Stop();

            if (result is not null)
            {
                if (result is CompressResult cr)
                {
                    result = (TResult)(object)(cr with { ElapsedMilliseconds = sw.ElapsedMilliseconds });
                }
                else if (result is ExtractResult er)
                {
                    result = (TResult)(object)(er with { ElapsedMilliseconds = sw.ElapsedMilliseconds });
                }

                if (isJson)
                {
                    CliJsonSerializer.Emit(result, outputWriter);
                }
                else if (renderConsoleSummary is not null)
                {
                    renderConsoleSummary(result, sw.ElapsedMilliseconds);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            return HandleException(ex, isJson, outputWriter, verboseErrors);
        }
    }

    public static int HandleException(Exception ex, bool isJson, TextWriter? writer = null, bool verboseErrors = false)
    {
        var actual = ex is CommandRuntimeException runtimeEx && runtimeEx.InnerException != null
            ? runtimeEx.InnerException
            : ex;

        return actual switch
        {
            CommandParseException parseEx => EmitError("ARGUMENT_ERROR", parseEx.Message, isJson, exitCode: 2, writer: writer, verboseErrors: verboseErrors),
            FileNotFoundException fnf => EmitError("SOURCE_NOT_FOUND", fnf.Message, isJson, exitCode: 2, writer: writer, verboseErrors: verboseErrors),
            DirectoryNotFoundException dnf => EmitError("SOURCE_NOT_FOUND", dnf.Message, isJson, exitCode: 2, writer: writer, verboseErrors: verboseErrors),
            SecurityException sec => EmitError("SECURITY_VIOLATION", sec.Message, isJson, exitCode: 1, writer: writer, verboseErrors: verboseErrors),
            ExtractionLimitExceededException elee => EmitError("EXECUTION_ERROR", elee.Message, isJson, exitCode: 1, writer: writer, verboseErrors: verboseErrors),
            NotSupportedException nse => EmitError("UNSUPPORTED_FORMAT", nse.Message, isJson, exitCode: 2, writer: writer, verboseErrors: verboseErrors),
            CommandAppException appEx => EmitError("ARGUMENT_ERROR", appEx.Message, isJson, exitCode: 2, writer: writer, verboseErrors: verboseErrors),
            ArgumentException argEx => EmitError("ARGUMENT_ERROR", argEx.Message, isJson, exitCode: 2, writer: writer, verboseErrors: verboseErrors),
            _ => EmitError("EXECUTION_ERROR", actual.Message, isJson, exitCode: 1, stackTrace: actual.StackTrace, writer: writer, verboseErrors: verboseErrors)
        };
    }

    public static int EmitError(string code, string message, bool isJson, int exitCode, string? stackTrace = null, TextWriter? writer = null, bool verboseErrors = false)
    {
        if (isJson)
        {
            CliJsonSerializer.EmitError(code, EntryNameSanitizer.Sanitize(message), verboseErrors ? stackTrace : null, writer);
        }
        else
        {
            var sanitized = EntryNameSanitizer.SingleLine(message);

            if (code == "SECURITY_VIOLATION")
            {
                AnsiConsole.MarkupLine($"[red]Security Violation:[/] {Markup.Escape(sanitized)}");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(sanitized)}");
            }
        }
        return exitCode;
    }
}

using System.Security;
using RusZip.Cli.Infrastructure;
using RusZip.Cli.Models;
using RusZip.Core.Engines;
using Spectre.Console.Cli;
using Xunit;

namespace RusZip.Cli.Tests;

public class CliCommandRunnerTests : CliTestBase
{
    private record SampleResult(string Status, int Count);

    [Fact]
    public async Task RunCli_CompressHelp_ReturnsZero()
    {
        var (exitCode, stdout) = await RunCliAsync("compress", "--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("USAGE", stdout);
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException), 2, "SOURCE_NOT_FOUND")]
    [InlineData(typeof(DirectoryNotFoundException), 2, "SOURCE_NOT_FOUND")]
    [InlineData(typeof(NotSupportedException), 2, "UNSUPPORTED_FORMAT")]
    [InlineData(typeof(ArgumentException), 2, "ARGUMENT_ERROR")]
    [InlineData(typeof(SecurityException), 1, "SECURITY_VIOLATION")]
    [InlineData(typeof(InvalidOperationException), 1, "EXECUTION_ERROR")]
    public void HandleException_MapsExceptionTypesToExpectedExitCodes(Type exType, int expectedExitCode, string expectedErrorCode)
    {
        var ex = (Exception)Activator.CreateInstance(exType, "Test error message")!;
        using var sw = new StringWriter();

        int code = CliCommandRunner.HandleException(ex, isJson: true, writer: sw);

        Assert.Equal(expectedExitCode, code);
        Assert.Contains(expectedErrorCode, sw.ToString());
    }

    [Fact]
    public void HandleException_ArchiveIntegrityException_MapsToExitCode1ExecutionError()
    {
        var ex = new ArchiveIntegrityException("CRC-32 mismatch for entry 'a.txt': expected 00000000, computed DDDD", "a.txt");
        using var sw = new StringWriter();

        int code = CliCommandRunner.HandleException(ex, isJson: true, writer: sw);

        Assert.Equal(1, code);
        var err = CliTestBase.ParseJson<ErrorResult>(sw.ToString());
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("CRC-32 mismatch", err.Error.Message);
    }

    [Fact]
    public void HandleException_UnwrapsCommandRuntimeException()
    {
        var ctors = typeof(CommandRuntimeException).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        var ctor = ctors.FirstOrDefault(c => c.GetParameters().Any(p => typeof(Exception).IsAssignableFrom(p.ParameterType))) ?? ctors.First();
        var inner = new FileNotFoundException("Missing file");
        var dummyArgs = ctor.GetParameters().Select(p =>
            typeof(Exception).IsAssignableFrom(p.ParameterType) ? (object)inner :
            p.ParameterType == typeof(string) ? (object)"Runtime wrapper" : null
        ).ToArray();
        var ex = (Exception)ctor.Invoke(dummyArgs);
        using var sw = new StringWriter();

        int code = CliCommandRunner.HandleException(ex, isJson: true, writer: sw);

        Assert.Equal(2, code);
        Assert.Contains("SOURCE_NOT_FOUND", sw.ToString());
    }

    [Fact]
    public void HandleException_UnwrapsCommandAppException_WrappingSecurityException_MapsToExit1SecurityViolation()
    {
        // F-17: a CommandAppException that is *not* a CommandRuntimeException (here
        // CommandConfigurationException) wrapping a SecurityException must unwrap to the inner cause
        // and map to SECURITY_VIOLATION (exit 1), not fall through to the wrapper's ARGUMENT_ERROR (2).
        var ex = CreateCommandAppException(new SecurityException("Elevated path denied"));

        using var sw = new StringWriter();

        int code = CliCommandRunner.HandleException(ex, isJson: true, writer: sw);

        Assert.Equal(1, code);
        var err = CliTestBase.ParseJson<ErrorResult>(sw.ToString());
        Assert.Equal("SECURITY_VIOLATION", err.Error.Code);
        Assert.Contains("Elevated path denied", err.Error.Message);
    }

    [Fact]
    public void HandleException_UnwrapsCommandAppException_WrappingFileNotFound_MapsToExit2SourceNotFound()
    {
        // F-17: uniform unwrapping must also preserve correct translation for a non-command error
        // wrapped inside a CommandAppException — the inner FileNotFoundException (not the wrapper)
        // drives the SOURCE_NOT_FOUND classification.
        var ex = CreateCommandAppException(new FileNotFoundException("Missing target"));

        using var sw = new StringWriter();

        int code = CliCommandRunner.HandleException(ex, isJson: true, writer: sw);

        Assert.Equal(2, code);
        var err = CliTestBase.ParseJson<ErrorResult>(sw.ToString());
        Assert.Equal("SOURCE_NOT_FOUND", err.Error.Code);
        Assert.Contains("Missing target", err.Error.Message);
    }

    [Fact]
    public void HandleException_CommandAppException_WithoutInner_MapsToExit2ArgumentError()
    {
        // A bare CommandAppException (no inner cause) must still map to ARGUMENT_ERROR (2).
        var ex = CreateCommandAppException(inner: null);

        using var sw = new StringWriter();

        int code = CliCommandRunner.HandleException(ex, isJson: true, writer: sw);

        Assert.Equal(2, code);
        Assert.Contains("ARGUMENT_ERROR", sw.ToString());
    }

    private static Exception CreateCommandAppException(Exception? inner, string message = "Command wrapper")
    {
        var ctor = typeof(CommandConfigurationException).GetConstructors(
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public)
            .FirstOrDefault(c => c.GetParameters().Any(p => typeof(Exception).IsAssignableFrom(p.ParameterType)))
            ?? throw new InvalidOperationException("No matching ctor on CommandConfigurationException");

        var args = ctor.GetParameters().Select(p =>
            typeof(Exception).IsAssignableFrom(p.ParameterType) ? (object?)inner :
            p.ParameterType == typeof(string) ? (object)message : null
        ).ToArray();

        return (Exception)ctor.Invoke(args);
    }

    [Fact]
    public async Task RunAsync_DirectInvocation_EmitsJsonToWriter()
    {
        using var sw = new StringWriter();

        int exitCode = await CliCommandRunner.RunAsync(
            "Test Op",
            isJson: true,
            operation: async (progress, ct) =>
            {
                await Task.Yield();
                return new SampleResult("ok", 42);
            },
            outputWriter: sw
        );

        Assert.Equal(0, exitCode);
        Assert.Contains("status", sw.ToString());
        Assert.Contains("42", sw.ToString());
    }

    [Fact]
    public async Task RunAsync_WhenExceptionThrown_TranslatesAndEmitsError()
    {
        using var sw = new StringWriter();

        int exitCode = await CliCommandRunner.RunAsync<SampleResult>(
            "Failing Op",
            isJson: true,
            operation: (progress, ct) => throw new FileNotFoundException("Target not found"),
            outputWriter: sw
        );

        Assert.Equal(2, exitCode);
        Assert.Contains("SOURCE_NOT_FOUND", sw.ToString());
        Assert.Contains("Target not found", sw.ToString());
    }

    [Fact]
    public async Task RunAsync_ConsoleMode_InvokesRenderConsoleSummary()
    {
        bool summaryRendered = false;
        long capturedElapsedMs = -1;

        int exitCode = await CliCommandRunner.RunAsync(
            "Console Op",
            isJson: false,
            operation: async (progress, ct) =>
            {
                await Task.Delay(10, ct);
                return new SampleResult("done", 100);
            },
            renderConsoleSummary: (result, elapsedMs) =>
            {
                summaryRendered = true;
                capturedElapsedMs = elapsedMs;
                Assert.Equal("done", result.Status);
                Assert.Equal(100, result.Count);
            }
        );

        Assert.Equal(0, exitCode);
        Assert.True(summaryRendered);
        Assert.True(capturedElapsedMs >= 0);
    }

    [Fact]
    public void EmitError_SecurityViolation_InHumanMode_Returns1()
    {
        int exitCode = CliCommandRunner.EmitError("SECURITY_VIOLATION", "Dangerous path", isJson: false, exitCode: 1);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void HandleException_JsonError_ExcludesStackTraceByDefault()
    {
        Exception captured;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        using var sw = new StringWriter();

        int code = CliCommandRunner.HandleException(captured, isJson: true, writer: sw);

        Assert.Equal(1, code);
        var err = CliTestBase.ParseJson<ErrorResult>(sw.ToString());
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Null(err.Error.Details);
        Assert.DoesNotContain("at RusZip.Cli", sw.ToString());
    }

    [Fact]
    public void HandleException_JsonError_WithVerboseErrors_IncludesStackTrace()
    {
        Exception captured;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        using var sw = new StringWriter();

        int code = CliCommandRunner.HandleException(captured, isJson: true, writer: sw, verboseErrors: true);

        Assert.Equal(1, code);
        var err = CliTestBase.ParseJson<ErrorResult>(sw.ToString());
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.NotNull(err.Error.Details);
        Assert.Contains("at RusZip.Cli.Tests", err.Error.Details);
    }

    [Fact]
    public void EmitError_JsonError_SanitizesControlBytesFromMessage()
    {
        using var sw = new StringWriter();

        int code = CliCommandRunner.EmitError(
            "EXECUTION_ERROR",
            "Failed on \u001b[31mRED\u001b[0m\u0000nul.txt",
            isJson: true,
            exitCode: 1,
            writer: sw);

        Assert.Equal(1, code);
        Assert.DoesNotContain('\u001b', sw.ToString());
        Assert.DoesNotContain('\u0000', sw.ToString());
    }
}

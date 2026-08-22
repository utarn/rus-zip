using System.Text.Json;
using RusZip.Cli;
using RusZip.Cli.Models;
using Spectre.Console;

namespace RusZip.Cli.Tests;

public abstract class CliTestBase : IDisposable
{
    private static readonly SemaphoreSlim Semaphore = new(1, 1);
    private readonly string _tempDirectory;

    protected CliTestBase()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ruszip_cli_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    protected string TempDirectory => _tempDirectory;

    protected string CreateTempFile(string filename, string content = "Sample test file content for rus-zip CLI.")
    {
        var filePath = Path.Combine(_tempDirectory, filename);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(filePath, content);
        return filePath;
    }

    protected string CreateTempDirectory(string dirname, int fileCount = 3)
    {
        var dirPath = Path.Combine(_tempDirectory, dirname);
        Directory.CreateDirectory(dirPath);
        for (int i = 1; i <= fileCount; i++)
        {
            File.WriteAllText(Path.Combine(dirPath, $"file_{i}.txt"), $"Content for file {i} inside {dirname}");
        }
        return dirPath;
    }

    protected static async Task<(int ExitCode, string StdOut)> RunCliAsync(params string[] args)
    {
        await Semaphore.WaitAsync();
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var originalConsole = AnsiConsole.Console;
        try
        {
            using var sw = new StringWriter();
            Console.SetOut(sw);
            Console.SetError(sw);
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(sw)
            });

            int exitCode = await Program.RunWithConsoleAsync(args, console);
            return (exitCode, sw.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
            AnsiConsole.Console = originalConsole;
            Semaphore.Release();
        }
    }

    protected static T ParseJson<T>(string output)
    {
        // Extract JSON portion in case any other output was written
        var trimmed = output.Trim();
        int firstBrace = trimmed.IndexOf('{');
        int lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace >= firstBrace)
        {
            var jsonText = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
            return JsonSerializer.Deserialize<T>(jsonText, CliJsonSerializer.Options)!;
        }

        return JsonSerializer.Deserialize<T>(output, CliJsonSerializer.Options)!;
    }

    public virtual void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup exceptions
        }
    }
}

using RusZip.Cli.Models;
using RusZip.Core.Models;
using RusZip.Core.Tests;

namespace RusZip.Cli.Tests;

/// <summary>
/// Output-hygiene regression tests (issue #43): control bytes, stack traces in JSON,
/// and strict JSON escaping. These assert on the raw stdout, not just parsed models.
/// </summary>
[Collection("CliTests")]
public sealed class OutputHygieneTests : CliTestBase
{
    private static bool ContainsDangerousControlByte(string text)
    {
        foreach (var ch in text)
        {
            // Flag C0/C1 controls (ESC, NUL, BEL, ...) but allow formatting whitespace
            // (\t, \n, \r) which legitimately appear in table layouts and pretty-printed JSON.
            if (ch < 32 && ch is not '\t' and not '\n' and not '\r') return true;
            if (ch == '\u007f' || (ch >= '\u0080' && ch <= '\u009f')) return true;
        }

        return false;
    }

    [Fact]
    public async Task List_EntryNameWithEscAndNul_HumanMode_ConsoleOutputHasZeroControlBytes()
    {
        // Arrange: zip entry name carries a raw ESC ANSI sequence and a NUL byte.
        var archivePath = Path.Combine(TempDirectory, "control_bytes.zip");
        TestArchiveFixtures.CreateZipArchiveWithEntryName(
            archivePath,
            "ok\u001b[31mRED\u001b[0m\u0000nul.txt",
            "payload");

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.False(ContainsDangerousControlByte(stdout), "Raw stdout must not contain any control bytes.");
        Assert.Contains("ok[31mRED[0mnul.txt", stdout);
    }

    [Fact]
    public async Task List_EntryNameWithEscAndNul_JsonMode_PathFieldStripsControlBytes()
    {
        // Arrange
        var archivePath = Path.Combine(TempDirectory, "control_bytes_json.zip");
        TestArchiveFixtures.CreateZipArchiveWithEntryName(
            archivePath,
            "ok\u001b[31mRED\u001b[0m\u0000nul.txt",
            "payload");

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.False(ContainsDangerousControlByte(stdout), "Raw stdout must not contain any control bytes.");

        var result = ParseJson<ListResult>(stdout);
        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("ok[31mRED[0mnul.txt", entry.Path);
    }

    [Fact]
    public async Task List_JsonOutput_EscapesAngleBracketsAndAmpersand()
    {
        // Arrange
        var archivePath = Path.Combine(TempDirectory, "html_special_chars.zip");
        TestArchiveFixtures.CreateZipArchiveWithEntryName(archivePath, "a<b&c>.txt", "payload");

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath, "--json");

        // Assert: strict escaping must encode '<', '&', '>' so JSON stays safe to embed/re-render.
        Assert.Equal(0, exitCode);
        Assert.Contains("\\u003C", stdout);
        Assert.Contains("\\u0026", stdout);
        Assert.Contains("\\u003E", stdout);
        Assert.Contains("a\\u003Cb\\u0026c\\u003E.txt", stdout);

        // The decoded model still carries the original characters.
        var result = ParseJson<ListResult>(stdout);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("a<b&c>.txt", entry.Path);
    }

    [Fact]
    public async Task List_CorruptedArchive_JsonError_ExcludesStackTraceByDefault()
    {
        // Arrange: Corrupted Zstd frame
        var corruptedArchive = Path.Combine(TempDirectory, "corrupted_no_trace.zrus");
        File.WriteAllBytes(corruptedArchive, [0x28, 0xB5, 0x2F, 0xFD, 0x00, 0xDE, 0xAD, 0xBE, 0xEF]);

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", corruptedArchive, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Null(err.Error.Details);
        Assert.DoesNotContain("at RusZip.", stdout);
    }

    [Fact]
    public async Task List_CorruptedArchive_JsonError_VerboseErrors_IncludesStackTrace()
    {
        // Arrange: Corrupted Zstd frame
        var corruptedArchive = Path.Combine(TempDirectory, "corrupted_verbose.zrus");
        File.WriteAllBytes(corruptedArchive, [0x28, 0xB5, 0x2F, 0xFD, 0x00, 0xDE, 0xAD, 0xBE, 0xEF]);

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", corruptedArchive, "--json", "--verbose-errors");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.NotNull(err.Error.Details);
        Assert.NotEmpty(err.Error.Details);
    }
}

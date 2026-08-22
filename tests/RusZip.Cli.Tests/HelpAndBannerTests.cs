using Xunit;

namespace RusZip.Cli.Tests;

[Collection("CliTests")]
public sealed class HelpAndBannerTests : CliTestBase
{
    [Fact]
    public async Task ZeroArgumentInvocation_ReturnsExitCode0_AndDisplaysFullHelpWithBannerAndProfiles()
    {
        // Act
        var (exitCode, stdout) = await RunCliAsync();

        // Assert
        if (exitCode != 0)
        {
            Assert.Fail($"ExitCode was {exitCode}, output:\n{stdout}");
        }
        Assert.Contains("rus-zip", stdout); // Figlet text or title
        Assert.Contains("USAGE:", stdout);
        Assert.Contains("COMPRESSION PROFILES (.zrus):", stdout);
        Assert.Contains("fast", stdout);
        Assert.Contains("balanced", stdout);
        Assert.Contains("high", stdout);
        Assert.Contains("ultra", stdout);
        Assert.Contains("SUPPORTED FORMATS:", stdout);
        Assert.Contains(".zrus (Tar+Zstd)", stdout);
        Assert.Contains(".zip", stdout);
        Assert.Contains(".rar", stdout);
        Assert.Contains(".7z", stdout);
        Assert.Contains("EXIT CODES:", stdout);
        Assert.Contains("0 = Success", stdout);
        Assert.Contains("1 = Execution / Engine error, Security violation", stdout);
        Assert.Contains("2 = Invalid arguments, Path not found, Unsupported format", stdout);
        Assert.Contains("--json", stdout);
        Assert.Contains("--no-overwrite", stdout);
        Assert.Contains("--max-uncompressed-size", stdout);
        Assert.Contains("--max-entries", stdout);
        Assert.Contains("--verbose-errors", stdout);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public async Task HelpFlag_ReturnsExitCode0_AndDisplaysFullHelp(string flag)
    {
        // Act
        var (exitCode, stdout) = await RunCliAsync(flag);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("COMPRESSION PROFILES", stdout);
        Assert.Contains("SUPPORTED FORMATS", stdout);
        Assert.Contains("EXIT CODES", stdout);
    }

    [Theory]
    [InlineData("compress", "--help")]
    [InlineData("extract", "--help")]
    [InlineData("list", "--help")]
    public async Task CommandHelp_ReturnsExitCode0_AndDisplaysCommandDetails(string command, string flag)
    {
        // Act
        var (exitCode, stdout) = await RunCliAsync(command, flag);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("USAGE:", stdout);
        Assert.Contains(command, stdout);
        Assert.Contains("EXAMPLES:", stdout);
    }

    [Fact]
    public async Task ExtractHelp_DocumentsNoOverwriteFlag()
    {
        // F-15/F-41: extract help must surface --no-overwrite (and the SEC-3 guardrail flags).
        var (exitCode, stdout) = await RunCliAsync("extract", "--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("--no-overwrite", stdout);
        Assert.Contains("--max-uncompressed-size", stdout);
        Assert.Contains("--max-entries", stdout);
    }

    [Fact]
    public async Task CompressHelp_DocumentsPerFormatLevelRange()
    {
        // F-16/F-41: compress help must document the per-format level range (.zip 0-9 with
        // 0 = Store, .zrus 1-22).
        var (exitCode, stdout) = await RunCliAsync("compress", "--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("0-9", stdout);
        Assert.Contains("0 = Store", stdout);
        Assert.Contains("1-22", stdout);
    }
}

using System.Text.Json;
using RusZip.Cli.Models;
using Xunit;

namespace RusZip.Cli.Tests;

[Collection("CliTests")]
public sealed class MultiVolumeCliTests : CliTestBase
{
    [Fact]
    public async Task Cli_CompressWithSplitOption_ProducesSequentialParts()
    {
        // Arrange
        var srcFile = CreateTempFile("bigfile.txt", new string('B', 300 * 1024));
        var zrusPath = Path.Combine(TempDirectory, "backup.zrus");

        // Act: compress with -s 64KB
        var (code, stdout) = await RunCliAsync("compress", srcFile, zrusPath, "-s", "64KB", "--json");

        // Assert
        Assert.Equal(0, code);
        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);

        var p1 = Path.Combine(TempDirectory, "backup.part1.zrus");
        var p2 = Path.Combine(TempDirectory, "backup.part2.zrus");
        Assert.True(File.Exists(p1));
        Assert.True(File.Exists(p2));
    }

    [Fact]
    public async Task Cli_CompressWithInvalidSplitSize_ReturnsExitCode2()
    {
        var srcFile = CreateTempFile("file.txt", "content");
        var zrusPath = Path.Combine(TempDirectory, "out.zrus");

        // Act: invalid split size "not_a_size"
        var (code, stdout) = await RunCliAsync("compress", srcFile, zrusPath, "-s", "not_a_size", "--json");

        // Assert
        Assert.Equal(2, code);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Contains("Invalid split size", err.Error.Message);
    }

    [Fact]
    public async Task Cli_Extract_MultiVolumeArchive_ExtractsCleanly()
    {
        // Arrange
        var srcFile = CreateTempFile("data.bin", new string('Q', 250 * 1024));
        var zrusPath = Path.Combine(TempDirectory, "split_cli.zrus");
        var extractDir = Path.Combine(TempDirectory, "split_cli_out");

        await RunCliAsync("compress", srcFile, zrusPath, "-s", "64KB", "--json");

        var part1 = Path.Combine(TempDirectory, "split_cli.part1.zrus");
        Assert.True(File.Exists(part1));

        // Act: extract starting from part1
        var (code, stdout) = await RunCliAsync("extract", part1, "-o", extractDir, "--json");

        // Assert
        Assert.Equal(0, code);
        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(1, result.ExtractedFiles);

        var extracted = Path.Combine(extractDir, "data.bin");
        Assert.True(File.Exists(extracted));
        Assert.Equal(250 * 1024, new FileInfo(extracted).Length);
    }
}

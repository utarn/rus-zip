using RusZip.Cli.Models;
using Xunit;

namespace RusZip.Cli.Tests;

[Collection("CliTests")]
public sealed class CompressCommandTests : CliTestBase
{
    [Fact]
    public async Task Compress_SingleFileToZrus_JsonMode_ReturnsExitCode0_AndValidJson()
    {
        // Arrange
        var sourceFile = CreateTempFile("sample.txt", "Hello rus-zip compress test");
        var destArchive = Path.Combine(TempDirectory, "output.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(destArchive));

        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(sourceFile), result.SourcePath);
        Assert.Equal(Path.GetFullPath(destArchive), result.ArchivePath);
        Assert.Equal("zrus", result.Format);
        Assert.Equal(1, result.TotalFiles);
        Assert.True(result.UncompressedBytes > 0);
        Assert.True(result.CompressedBytes > 0);
        Assert.True(result.CompressionRatio > 0);
        Assert.True(result.ElapsedMilliseconds >= 0);
    }

    [Fact]
    public async Task Compress_DirectoryToZrus_JsonMode_ReturnsExitCode0()
    {
        // Arrange
        var sourceDir = CreateTempDirectory("sample_dir", fileCount: 4);
        var destArchive = Path.Combine(TempDirectory, "dir_output.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceDir, destArchive, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(destArchive));

        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(sourceDir), result.SourcePath);
        Assert.Equal(4, result.TotalFiles);
    }

    [Fact]
    public async Task Compress_ToZip_JsonMode_ReturnsExitCode0()
    {
        // Arrange
        var sourceFile = CreateTempFile("zip_input.txt", "Data to compress in zip format");
        var destArchive = Path.Combine(TempDirectory, "archive.zip");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(destArchive));

        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal("zip", result.Format);
        Assert.Equal(1, result.TotalFiles);
    }

    [Theory]
    [InlineData("fast")]
    [InlineData("balanced")]
    [InlineData("high")]
    [InlineData("ultra")]
    public async Task Compress_WithNamedProfiles_ReturnsExitCode0(string profile)
    {
        // Arrange
        var sourceFile = CreateTempFile($"file_{profile}.txt", $"Data for profile {profile}");
        var destArchive = Path.Combine(TempDirectory, $"archive_{profile}.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "-p", profile, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(destArchive));

        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(22)]
    public async Task Compress_WithExplicitLevels_ReturnsExitCode0(int level)
    {
        // Arrange
        var sourceFile = CreateTempFile($"level_{level}.txt", $"Data for level {level}");
        var destArchive = Path.Combine(TempDirectory, $"archive_lvl_{level}.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "-l", level.ToString(), "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(destArchive));

        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Compress_DefaultDestination_CreatesZrusFile()
    {
        // Arrange
        var sourceFile = CreateTempFile("default_dest.txt", "Testing default destination naming");
        var expectedDest = sourceFile + ".zrus";

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(expectedDest));

        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(expectedDest), result.ArchivePath);
    }

    [Fact]
    public async Task Compress_HumanMode_ReturnsExitCode0_AndDisplaysSummary()
    {
        // Arrange
        var sourceFile = CreateTempFile("human.txt", "Human readable CLI output test");
        var destArchive = Path.Combine(TempDirectory, "human.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(destArchive));
        Assert.Contains("Compression Summary", stdout);
        Assert.Contains("Archive Path", stdout);
        Assert.Contains("Total Files", stdout);
    }

    [Fact]
    public async Task Compress_NonExistentSource_ReturnsExitCode2_AndSourceNotFoundJson()
    {
        // Arrange
        var nonExistent = Path.Combine(TempDirectory, "missing_file_xyz.txt");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", nonExistent, "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("SOURCE_NOT_FOUND", err.Error.Code);
        Assert.Contains(nonExistent, err.Error.Message);
    }

    [Fact]
    public async Task Compress_InvalidProfile_ReturnsExitCode2_AndArgumentErrorJson()
    {
        // Arrange
        var sourceFile = CreateTempFile("valid.txt", "Valid file");
        var destArchive = Path.Combine(TempDirectory, "out.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "-p", "invalid_profile", "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
        Assert.Contains("Invalid compression profile", err.Error.Message);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(100)]
    public async Task Compress_InvalidLevel_ReturnsExitCode2_AndArgumentErrorJson(int invalidLevel)
    {
        // Arrange
        var sourceFile = CreateTempFile("valid2.txt", "Valid file");
        var destArchive = Path.Combine(TempDirectory, "out2.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "-l", invalidLevel.ToString(), "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
        Assert.Contains("Compression level must be between", err.Error.Message);
    }

    [Theory]
    [InlineData("output.rar")]
    [InlineData("output.7z")]
    [InlineData("output.unsupported")]
    public async Task Compress_UnsupportedCreationFormat_ReturnsExitCode2_AndUnsupportedFormatJson(string destName)
    {
        // Arrange
        var sourceFile = CreateTempFile("valid3.txt", "Valid file");
        var destArchive = Path.Combine(TempDirectory, destName);

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("UNSUPPORTED_FORMAT", err.Error.Code);
    }

    [Fact]
    public async Task Compress_MissingRequiredArguments_ReturnsExitCode2_AndArgumentErrorJson()
    {
        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
    }
}

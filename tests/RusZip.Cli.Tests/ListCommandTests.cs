using RusZip.Cli.Models;
using RusZip.Core.Tests;
using Xunit;

namespace RusZip.Cli.Tests;

[Collection("CliTests")]
public sealed class ListCommandTests : CliTestBase
{
    [Fact]
    public async Task List_ZrusArchive_JsonMode_ReturnsExitCode0_AndValidJson()
    {
        // Arrange
        var sourceDir = CreateTempDirectory("list_zrus_dir", fileCount: 3);
        var archivePath = Path.Combine(TempDirectory, "test_list.zrus");
        await RunCliAsync("compress", sourceDir, archivePath, "--json");

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath, "--json");

        // Assert
        Assert.Equal(0, exitCode);

        var result = ParseJson<ListResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(archivePath), result.ArchivePath);
        Assert.Equal("zrus", result.Format);
        Assert.True(result.TotalEntries >= 3);
        Assert.NotEmpty(result.Entries);
        Assert.All(result.Entries, entry =>
        {
            Assert.False(string.IsNullOrEmpty(entry.Path));
        });
    }

    [Fact]
    public async Task List_ZipArchive_JsonMode_ReturnsExitCode0()
    {
        // Arrange
        var sourceFile = CreateTempFile("list_zip.txt", "Zip listing test content");
        var archivePath = Path.Combine(TempDirectory, "test_list.zip");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath, "--json");

        // Assert
        Assert.Equal(0, exitCode);

        var result = ParseJson<ListResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal("zip", result.Format);
        Assert.Single(result.Entries);
        Assert.Equal("list_zip.txt", result.Entries[0].Path);
        Assert.False(result.Entries[0].IsDirectory);
    }

    [Fact]
    public async Task List_TarGzArchive_ReturnsExitCode0()
    {
        // Arrange
        var archivePath = Path.Combine(TempDirectory, "test_list.tar.gz");
        await TestArchiveFixtures.CreateTarGzArchiveAsync(archivePath, new Dictionary<string, string>
        {
            ["alpha.txt"] = "Alpha",
            ["beta.txt"] = "Beta"
        });

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath, "--json");

        // Assert
        Assert.Equal(0, exitCode);

        var result = ParseJson<ListResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal("tar.gz", result.Format);
        Assert.Equal(2, result.TotalEntries);
    }

    [Fact]
    public async Task List_SevenZipArchive_ReturnsExitCode0()
    {
        // Arrange
        var archivePath = Path.Combine(TempDirectory, "test_list.7z");
        TestArchiveFixtures.CreateSevenZipArchive(archivePath, "seven_list.txt", "7z list data");

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath, "--json");

        // Assert
        Assert.Equal(0, exitCode);

        var result = ParseJson<ListResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal("7z", result.Format);
        Assert.Single(result.Entries);
    }

    [Fact]
    public async Task List_RarArchive_ReturnsExitCode0()
    {
        // Arrange
        var archivePath = Path.Combine(TempDirectory, "test_list.rar");
        TestArchiveFixtures.CreateRar4Archive(archivePath, "rar_list.txt", "RAR list data");

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath, "--json");

        // Assert
        Assert.Equal(0, exitCode);

        var result = ParseJson<ListResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal("rar", result.Format);
        Assert.Single(result.Entries);
    }

    [Fact]
    public async Task List_HumanMode_DisplaysSpectreTable()
    {
        // Arrange
        var sourceFile = CreateTempFile("human_list.txt", "Human list content");
        var archivePath = Path.Combine(TempDirectory, "human_list.zrus");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("Path", stdout);
        Assert.Contains("Size", stdout);
        Assert.Contains("Modified", stdout);
        Assert.Contains("human_list.txt", stdout);
        Assert.Contains("Total: 1 entries", stdout);
    }

    [Fact]
    public async Task List_NonExistentArchive_ReturnsExitCode2_AndSourceNotFoundJson()
    {
        // Arrange
        var nonExistent = Path.Combine(TempDirectory, "missing_list.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", nonExistent, "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("SOURCE_NOT_FOUND", err.Error.Code);
    }

    [Fact]
    public async Task List_UnsupportedFormat_ReturnsExitCode2_AndUnsupportedFormatJson()
    {
        // Arrange
        var invalidFormat = CreateTempFile("file.notanarchive", "Data");

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", invalidFormat, "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("UNSUPPORTED_FORMAT", err.Error.Code);
    }

    [Fact]
    public async Task List_MissingArguments_ReturnsExitCode2_AndArgumentErrorJson()
    {
        // Act
        var (exitCode, stdout) = await RunCliAsync("list", "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
    }

    [Fact]
    public async Task List_CorruptedArchive_ReturnsExitCode1_AndExecutionErrorJson()
    {
        // Arrange: Corrupted Zstd frame
        var corruptedArchive = Path.Combine(TempDirectory, "corrupted_list.zrus");
        File.WriteAllBytes(corruptedArchive, [0x28, 0xB5, 0x2F, 0xFD, 0x00, 0xDE, 0xAD, 0xBE, 0xEF]);

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", corruptedArchive, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
    }
}

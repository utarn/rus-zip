using System.Text.Json;
using RusZip.Cli.Models;
using Xunit;

namespace RusZip.Cli.Tests;

[Collection("CliTests")]
public sealed class PasswordCliTests : CliTestBase
{
    [Fact]
    public async Task Cli_CompressAndExtract_WithPasswordOption_Succeeds()
    {
        // Arrange
        var srcFile = CreateTempFile("doc.txt", "Encrypted through CLI with -p password");
        var zrusPath = Path.Combine(TempDirectory, "cli_vault.zrus");
        var extractDir = Path.Combine(TempDirectory, "cli_extracted");

        const string password = "CliPassword$2026";

        // Act 1: Compress with --password
        var (compressCode, compressOut) = await RunCliAsync("compress", srcFile, zrusPath, "--password", password, "--json");
        Assert.Equal(0, compressCode);
        Assert.True(File.Exists(zrusPath));

        var compressResult = ParseJson<CompressResult>(compressOut);
        Assert.True(compressResult.Success);

        // Act 2: Extract with -p
        var (extractCode, extractOut) = await RunCliAsync("extract", zrusPath, "-o", extractDir, "-p", password, "--json");
        Assert.Equal(0, extractCode);

        var extractResult = ParseJson<ExtractResult>(extractOut);
        Assert.True(extractResult.Success);
        Assert.Equal(1, extractResult.ExtractedFiles);

        var extractedFile = Path.Combine(extractDir, "doc.txt");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal("Encrypted through CLI with -p password", await File.ReadAllTextAsync(extractedFile));
    }

    [Fact]
    public async Task Cli_Extract_EncryptedArchiveWithoutPassword_InJsonMode_FailsWithExitCode1()
    {
        // Arrange
        var srcFile = CreateTempFile("secret.txt", "Classified data");
        var zrusPath = Path.Combine(TempDirectory, "no_pwd.zrus");
        var extractDir = Path.Combine(TempDirectory, "no_pwd_out");

        await RunCliAsync("compress", srcFile, zrusPath, "--password", "SecretPass123", "--json");
        Assert.True(File.Exists(zrusPath));

        // Act: extract without -p in --json mode
        var (extractCode, extractOut) = await RunCliAsync("extract", zrusPath, "-o", extractDir, "--json");

        // Assert: fails fast with exit code 1
        Assert.Equal(1, extractCode);
        var err = ParseJson<ErrorResult>(extractOut);
        Assert.False(err.Success);
        Assert.Contains("Password required", err.Error.Message);
    }

    [Fact]
    public async Task Cli_List_EncryptedArchiveWithPassword_ListsEntries()
    {
        // Arrange
        var srcFile = CreateTempFile("list_item.txt", "Item content");
        var zrusPath = Path.Combine(TempDirectory, "list_vault.zrus");

        await RunCliAsync("compress", srcFile, zrusPath, "--password", "ListPass99", "--json");

        // Act: list with -p
        var (listCode, listOut) = await RunCliAsync("list", zrusPath, "-p", "ListPass99", "--json");

        // Assert
        Assert.Equal(0, listCode);
        var listResult = ParseJson<ListResult>(listOut);
        Assert.True(listResult.Success);
        Assert.Single(listResult.Entries);
        Assert.Equal("list_item.txt", listResult.Entries[0].Path);
    }
}

using RusZip.Cli.Models;
using RusZip.Core.Tests;
using Xunit;

namespace RusZip.Cli.Tests;

[Collection("CliTests")]
public sealed class ExtractCommandTests : CliTestBase
{
    [Fact]
    public async Task Extract_ZrusArchive_JsonMode_ReturnsExitCode0_AndValidJson()
    {
        // Arrange: create and compress a file first
        var sourceFile = CreateTempFile("doc.txt", "Document to extract");
        var archivePath = Path.Combine(TempDirectory, "test_extract.zrus");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        var outDir = Path.Combine(TempDirectory, "extracted_zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(outDir, "doc.txt")));

        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(archivePath), result.ArchivePath);
        Assert.Equal(Path.GetFullPath(outDir), result.DestinationPath);
        Assert.Equal(1, result.ExtractedFiles);
        Assert.True(result.TotalBytes > 0);
    }

    [Fact]
    public async Task Extract_ZipArchive_JsonMode_ReturnsExitCode0()
    {
        // Arrange
        var sourceFile = CreateTempFile("zipped.txt", "Zipped content");
        var archivePath = Path.Combine(TempDirectory, "test.zip");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        var outDir = Path.Combine(TempDirectory, "extracted_zip");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(outDir, "zipped.txt")));

        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(1, result.ExtractedFiles);
    }

    [Fact]
    public async Task Extract_TarGzArchive_ReturnsExitCode0()
    {
        // Arrange
        var archivePath = Path.Combine(TempDirectory, "test.tar.gz");
        await TestArchiveFixtures.CreateTarGzArchiveAsync(archivePath, new Dictionary<string, string>
        {
            ["file1.txt"] = "Content 1",
            ["sub/file2.txt"] = "Content 2"
        });

        var outDir = Path.Combine(TempDirectory, "extracted_targz");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(outDir, "file1.txt")));
        Assert.True(File.Exists(Path.Combine(outDir, "sub", "file2.txt")));

        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Extract_GzArchive_ReturnsExitCode0()
    {
        // Arrange
        var archivePath = Path.Combine(TempDirectory, "singlefile.txt.gz");
        await TestArchiveFixtures.CreateGzArchiveAsync(archivePath, "Gz Single File Content");

        var outDir = Path.Combine(TempDirectory, "extracted_gz");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(outDir, "singlefile.txt")));

        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Extract_SevenZipArchive_ReturnsExitCode0()
    {
        // Arrange
        var archivePath = Path.Combine(TempDirectory, "test.7z");
        TestArchiveFixtures.CreateSevenZipArchive(archivePath, "7z_inner.txt", "7-Zip extraction test");

        var outDir = Path.Combine(TempDirectory, "extracted_7z");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(outDir, "7z_inner.txt")));

        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Extract_RarArchive_ReturnsExitCode0()
    {
        // Arrange
        var archivePath = Path.Combine(TempDirectory, "test.rar");
        TestArchiveFixtures.CreateRar4Archive(archivePath, "rar_inner.txt", "RAR extraction test");

        var outDir = Path.Combine(TempDirectory, "extracted_rar");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(outDir, "rar_inner.txt")));

        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("-f")]
    [InlineData("--force")]
    [InlineData("--overwrite")]
    public async Task Extract_WithForceOverwriteFlag_OverwritesExistingFile(string flag)
    {
        // Arrange
        var sourceFile = CreateTempFile("overwrite_me.txt", "Version 2 Content");
        var archivePath = Path.Combine(TempDirectory, "test_force.zrus");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        var outDir = Path.Combine(TempDirectory, "extracted_force");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "overwrite_me.txt"), "Version 1 Content");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, flag, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal("Version 2 Content", File.ReadAllText(Path.Combine(outDir, "overwrite_me.txt")));
    }

    [Fact]
    public async Task Extract_HumanMode_DisplaysSummaryPanel()
    {
        // Arrange
        var sourceFile = CreateTempFile("human_ext.txt", "Human extraction");
        var archivePath = Path.Combine(TempDirectory, "human_ext.zrus");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        var outDir = Path.Combine(TempDirectory, "human_extracted");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("Extraction Summary", stdout);
        Assert.Contains("Files Extracted", stdout);
        Assert.Contains("Total Extracted Size", stdout);
    }

    [Fact]
    public async Task Extract_NonExistentArchive_ReturnsExitCode2_AndSourceNotFoundJson()
    {
        // Arrange
        var nonExistent = Path.Combine(TempDirectory, "missing_archive.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", nonExistent, "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("SOURCE_NOT_FOUND", err.Error.Code);
    }

    [Fact]
    public async Task Extract_UnsupportedFormat_ReturnsExitCode2_AndUnsupportedFormatJson()
    {
        // Arrange
        var unsupportedFile = CreateTempFile("data.unknown", "Unknown format");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", unsupportedFile, "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("UNSUPPORTED_FORMAT", err.Error.Code);
    }

    [Fact]
    public async Task Extract_MissingArguments_ReturnsExitCode2_AndArgumentErrorJson()
    {
        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
    }

    [Fact]
    public async Task Extract_CorruptedArchive_ReturnsExitCode1_AndExecutionErrorJson()
    {
        // Arrange: Corrupted Zstd header in .zrus file
        var corruptedArchive = Path.Combine(TempDirectory, "corrupted.zrus");
        File.WriteAllBytes(corruptedArchive, [0x28, 0xB5, 0x2F, 0xFD, 0x00, 0xFF, 0xFF, 0xAA]);

        var outDir = Path.Combine(TempDirectory, "extracted_corrupted");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", corruptedArchive, "-o", outDir, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
    }

    [Fact]
    public async Task Extract_ZipSlipSecurityViolation_ReturnsExitCode1_AndSecurityViolationJson()
    {
        // Arrange: malicious zip slip archive
        var maliciousArchive = Path.Combine(TempDirectory, "zipslip.zip");
        TestArchiveFixtures.CreateZipSlipArchive(maliciousArchive, "../../evil.txt", "evil data");

        var outDir = Path.Combine(TempDirectory, "extracted_slip");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", maliciousArchive, "-o", outDir, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("SECURITY_VIOLATION", err.Error.Code);
    }
}

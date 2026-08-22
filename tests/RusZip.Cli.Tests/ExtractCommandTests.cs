using System.IO.Compression;
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

    [Fact]
    public async Task Extract_BombZip_WithMaxUncompressedSizeCap_ReturnsExitCode1_AndExecutionError()
    {
        // Arrange: a 2 MB stored zip entry against a 1 MB cap
        var archivePath = Path.Combine(TempDirectory, "bomb_size.zip");
        CreateZipWithEntries(archivePath, ("big.bin", new byte[2 * 1024 * 1024]));

        var outDir = Path.Combine(TempDirectory, "extracted_bomb_size");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--max-uncompressed-size", "1MB", "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("uncompressed output", err.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--max-uncompressed-size", err.Error.Message, StringComparison.OrdinalIgnoreCase);
        // Partial output cleaned up.
        Assert.False(File.Exists(Path.Combine(outDir, "big.bin")));
    }

    [Fact]
    public async Task Extract_BombZip_WithOverrideFlag_Completes_AndReportsRealTotals()
    {
        // Arrange
        var payload = new byte[2 * 1024 * 1024];
        Random.Shared.NextBytes(payload);
        var archivePath = Path.Combine(TempDirectory, "bomb_override.zip");
        CreateZipWithEntries(archivePath, ("big.bin", payload));

        var outDir = Path.Combine(TempDirectory, "extracted_bomb_override");

        // Act: override the cap (human-readable form) so the archive completes
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--max-uncompressed-size", "10MB", "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(outDir, "big.bin")));
        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(payload.Length, (int)result.TotalBytes);
        Assert.Equal(1, result.ExtractedFiles);
    }

    [Fact]
    public async Task Extract_InvalidMaxUncompressedSize_ReturnsExitCode2_AndArgumentErrorJson()
    {
        // Arrange
        var sourceFile = CreateTempFile("ok.txt", "content");
        var archivePath = Path.Combine(TempDirectory, "ok.zrus");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        var outDir = Path.Combine(TempDirectory, "extracted_bad_flag");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--max-uncompressed-size", "not-a-size", "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
    }

    [Fact]
    public async Task Extract_EntryCountCapExceeded_ReturnsExitCode1_AndExecutionError()
    {
        // Arrange: a zip with 5 entries against a cap of 2
        var archivePath = Path.Combine(TempDirectory, "bomb_entries.zip");
        CreateZipWithEntries(
            archivePath,
            ("file0.txt", "zero"u8.ToArray()),
            ("file1.txt", "one"u8.ToArray()),
            ("file2.txt", "two"u8.ToArray()),
            ("file3.txt", "three"u8.ToArray()),
            ("file4.txt", "four"u8.ToArray()));

        var outDir = Path.Combine(TempDirectory, "extracted_bomb_entries");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--max-entries", "2", "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("entry count", err.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--max-entries", err.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Extract_ReportsActualExtractedSize_NotHeaderMetadata()
    {
        // Arrange: a normal small archive
        var payload = "hello actual payload"u8.ToArray();
        var archivePath = Path.Combine(TempDirectory, "real_totals.zip");
        CreateZipWithEntries(archivePath, ("data.bin", payload));

        var outDir = Path.Combine(TempDirectory, "extracted_real_totals");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(payload.Length, (int)result.TotalBytes);
        Assert.Equal(1, result.ExtractedFiles);
    }

    private static void CreateZipWithEntries(string archivePath, params (string Name, byte[] Content)[] entries)
    {
        using var fs = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
            using var es = entry.Open();
            es.Write(content);
        }
    }
}

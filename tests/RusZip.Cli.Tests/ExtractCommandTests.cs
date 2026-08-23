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
    public async Task Extract_TarZstdArchive_MultiFile_JsonMode_ReturnsExitCode0_AndValidJson()
    {
        // Arrange: create multi-file directory and compress to .tar.zstd
        var sourceDir = CreateTempDirectory("src_tarzstd", fileCount: 3);
        var archivePath = Path.Combine(TempDirectory, "test_extract.tar.zstd");
        var (cCode, _) = await RunCliAsync("compress", sourceDir, archivePath, "--json");
        Assert.Equal(0, cCode);

        var outDir = Path.Combine(TempDirectory, "extracted_tarzstd");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(archivePath), result.ArchivePath);
        Assert.Equal(Path.GetFullPath(outDir), result.DestinationPath);
        Assert.Equal(3, result.ExtractedFiles);
        Assert.True(result.TotalBytes > 0);
    }

    [Fact]
    public async Task Extract_TzstdArchive_MultiFile_JsonMode_ReturnsExitCode0_AndValidJson()
    {
        // Arrange: create multi-file directory and compress to .tzstd
        var sourceDir = CreateTempDirectory("src_tzstd", fileCount: 2);
        var archivePath = Path.Combine(TempDirectory, "test_extract.tzstd");
        var (cCode, _) = await RunCliAsync("compress", sourceDir, archivePath, "--json");
        Assert.Equal(0, cCode);

        var outDir = Path.Combine(TempDirectory, "extracted_tzstd");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(archivePath), result.ArchivePath);
        Assert.Equal(Path.GetFullPath(outDir), result.DestinationPath);
        Assert.Equal(2, result.ExtractedFiles);
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
    public async Task Extract_NoOverwrite_ExistingFile_ReturnsExitCode1_AndExecutionErrorNamingPath()
    {
        // F-15: with --no-overwrite, an existing destination file must abort (exit 1) and name
        // the conflicting path instead of overwriting.
        var sourceFile = CreateTempFile("no_overwrite.txt", "Archive content");
        var archivePath = Path.Combine(TempDirectory, "no_overwrite.zrus");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        var outDir = Path.Combine(TempDirectory, "extracted_no_overwrite");
        Directory.CreateDirectory(outDir);
        var conflict = Path.Combine(outDir, "no_overwrite.txt");
        File.WriteAllText(conflict, "pre-existing content");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--no-overwrite", "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains(conflict, err.Error.Message);
        Assert.Equal("pre-existing content", File.ReadAllText(conflict)); // untouched
    }

    [Fact]
    public async Task Extract_NoOverwrite_NoConflict_Succeeds()
    {
        // F-15: --no-overwrite must not block extraction when no destination file exists.
        var sourceFile = CreateTempFile("no_conflict.txt", "Content");
        var archivePath = Path.Combine(TempDirectory, "no_conflict.zrus");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        var outDir = Path.Combine(TempDirectory, "extracted_no_conflict");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--no-overwrite", "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(outDir, "no_conflict.txt")));
        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(1, result.ExtractedFiles);
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

    [Fact]
    public async Task Extract_CorruptedZrus_MidStream_ReturnsExitCode1_AndExecutionError_AndCleansUp()
    {
        // Arrange: a large incompressible file so a mid-stream byte flip corrupts compressed data
        // (F-08: previously extracted silently with exit 0 and different content).
        var sourceFile = Path.Combine(TempDirectory, "midstream_cli.bin");
        var payload = new byte[5 * 1024 * 1024];
        new Random(4242).NextBytes(payload);
        await File.WriteAllBytesAsync(sourceFile, payload);

        var archivePath = Path.Combine(TempDirectory, "midstream_cli.zrus");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        var bytes = await File.ReadAllBytesAsync(archivePath);
        bytes[bytes.Length / 2] ^= 0xFF;
        var badPath = Path.Combine(TempDirectory, "midstream_cli_bad.zrus");
        await File.WriteAllBytesAsync(badPath, bytes);

        var outDir = Path.Combine(TempDirectory, "midstream_cli_out");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", badPath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("checksum", err.Error.Message, StringComparison.OrdinalIgnoreCase);
        // Partial (corrupt) output cleaned up.
        Assert.False(File.Exists(Path.Combine(outDir, "midstream_cli.bin")));
        Assert.Empty(Directory.GetFiles(outDir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Extract_CorruptedTarZstd_MidStream_ReturnsExitCode1_AndExecutionError_AndCleansUp()
    {
        var sourceFile = Path.Combine(TempDirectory, "midstream_tarzstd.bin");
        var payload = new byte[5 * 1024 * 1024];
        new Random(4242).NextBytes(payload);
        await File.WriteAllBytesAsync(sourceFile, payload);

        var archivePath = Path.Combine(TempDirectory, "midstream_tarzstd.tar.zstd");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        var bytes = await File.ReadAllBytesAsync(archivePath);
        bytes[bytes.Length / 2] ^= 0xFF;
        var badPath = Path.Combine(TempDirectory, "midstream_tarzstd_bad.tar.zstd");
        await File.WriteAllBytesAsync(badPath, bytes);

        var outDir = Path.Combine(TempDirectory, "midstream_tarzstd_out");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", badPath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("checksum", err.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(outDir, "midstream_tarzstd.bin")));
        Assert.Empty(Directory.GetFiles(outDir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Extract_TarZstd_WithMaxUncompressedSizeCap_ReturnsExitCode1_AndExecutionError()
    {
        var sourceFile = Path.Combine(TempDirectory, "big_tarzstd.bin");
        var payload = new byte[2 * 1024 * 1024];
        new Random(1234).NextBytes(payload);
        await File.WriteAllBytesAsync(sourceFile, payload);

        var archivePath = Path.Combine(TempDirectory, "bomb_size.tar.zstd");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        var outDir = Path.Combine(TempDirectory, "extracted_tarzstd_bomb_size");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--max-uncompressed-size", "1MB", "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("uncompressed output", err.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(outDir, "big_tarzstd.bin")));
    }

    [Fact]
    public async Task Extract_TarZstd_EntryCountCapExceeded_ReturnsExitCode1_AndExecutionError()
    {
        var srcDir = CreateTempDirectory("tarzstd_entries_src", fileCount: 5);
        var archivePath = Path.Combine(TempDirectory, "bomb_entries.tar.zstd");
        await RunCliAsync("compress", srcDir, archivePath, "--json");

        var outDir = Path.Combine(TempDirectory, "extracted_tarzstd_bomb_entries");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--max-entries", "2", "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("entry count", err.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Extract_ZipCrcMismatch_ReturnsExitCode1_AndExecutionError_AndCleansUp()
    {
        // F-09: a byte-patched local-data region previously extracted silently with exit 0.
        var validZip = TestArchiveFixtures.BuildStoreZip(("a.txt", "AAAAA"), ("b.txt", "BBBBBBBBBB"));
        var patched = TestArchiveFixtures.FlipByteInFirstLocalData(validZip);
        var archivePath = Path.Combine(TempDirectory, "crc_mismatch.zip");
        await File.WriteAllBytesAsync(archivePath, patched);
        var outDir = Path.Combine(TempDirectory, "crc_mismatch_out");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("CRC-32", err.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(outDir, "a.txt")));
        Assert.Empty(Directory.GetFiles(outDir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Extract_ZipBrokenCentralDirectory_ReturnsExitCode1_AndExecutionError()
    {
        // F-10: EOCD declares entries but the central directory is zeroed.
        var validZip = TestArchiveFixtures.BuildStoreZip(("a.txt", "AAAAA"), ("b.txt", "BBBBBBBBBB"));
        var broken = TestArchiveFixtures.ZeroCentralDirectoryRegion(validZip);
        var archivePath = Path.Combine(TempDirectory, "broken_cd_cli.zip");
        await File.WriteAllBytesAsync(archivePath, broken);
        var outDir = Path.Combine(TempDirectory, "broken_cd_cli_out");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("central directory", err.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(outDir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Extract_ZipBrokenEocd_ReturnsExitCode1_AndExecutionError()
    {
        var validZip = TestArchiveFixtures.BuildStoreZip(("a.txt", "AAAAA"));
        var broken = TestArchiveFixtures.CorruptEocdSignature(validZip);
        var archivePath = Path.Combine(TempDirectory, "broken_eocd_cli.zip");
        await File.WriteAllBytesAsync(archivePath, broken);
        var outDir = Path.Combine(TempDirectory, "broken_eocd_cli_out");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("corrupted or unparseable", err.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Extract_ZipNameLengthCorruption_ReturnsExitCode1_AndExecutionError_AndCleansUp()
    {
        var validZip = TestArchiveFixtures.BuildStoreZip(("a.txt", "AAAAA"), ("b.txt", "BBBBBBBBBB"));
        var broken = TestArchiveFixtures.CorruptFirstNameLength(validZip);
        var archivePath = Path.Combine(TempDirectory, "name_len_cli.zip");
        await File.WriteAllBytesAsync(archivePath, broken);
        var outDir = Path.Combine(TempDirectory, "name_len_cli_out");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("corrupted", err.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(outDir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Extract_ZipEntryWithNullCharacterInName_ReturnsExitCode1_AndExecutionError_AndCleansUp()
    {
        // F-10 (translation half): a corrupt archive whose entry name contains a NUL byte surfaces as
        // ArgumentException ("Null character in path") from path resolution inside the engine. Because
        // the failure originates from archive data (engine boundary) — not a user-supplied bad path —
        // it must map to EXECUTION_ERROR (1), not ARGUMENT_ERROR (2), and partial output must be cleaned up.
        var archivePath = Path.Combine(TempDirectory, "null_char_entry.zip");
        TestArchiveFixtures.CreateZipArchiveWithEntryName(archivePath, "bad\u0000name.txt", "payload");
        var outDir = Path.Combine(TempDirectory, "null_char_out");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("invalid path", err.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(outDir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Extract_EmptyZip_ReturnsExitCode0()
    {
        // A genuinely empty zip must keep working (zero-entry success is legal only for empty archives).
        var emptyZip = TestArchiveFixtures.BuildStoreZip();
        var archivePath = Path.Combine(TempDirectory, "empty_cli.zip");
        await File.WriteAllBytesAsync(archivePath, emptyZip);
        var outDir = Path.Combine(TempDirectory, "empty_cli_out");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(0, result.ExtractedFiles);
    }

    [Fact]
    public async Task Extract_EmptyZrusArchive_ReturnsExitCode0_AndCreatesEmptyDestination()
    {
        // F-11: an empty directory compressed to .zrus must extract to an empty destination with
        // exit 0 — parity with the .zip path.
        var sourceDir = Path.Combine(TempDirectory, "extract_empty_zrus_src");
        Directory.CreateDirectory(sourceDir);
        var archivePath = Path.Combine(TempDirectory, "extract_empty.zrus");
        await RunCliAsync("compress", sourceDir, archivePath, "--json");

        var outDir = Path.Combine(TempDirectory, "extract_empty_zrus_out");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(Directory.Exists(outDir));
        Assert.Empty(Directory.GetFileSystemEntries(outDir));

        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(0, result.ExtractedFiles);
    }

    [Fact]
    public async Task Extract_LegacyEmptyZrus_ReturnsExitCode0_AndCreatesEmptyDestination()
    {
        // F-11 read-side compat: the pre-fix 13-byte empty frame must extract to an empty
        // destination with exit 0, not an integrity error.
        var archivePath = Path.Combine(TempDirectory, "legacy_empty_extract.zrus");
        await File.WriteAllBytesAsync(archivePath, [0x28, 0xB5, 0x2F, 0xFD, 0x24, 0x00, 0x01, 0x00, 0x00, 0x99, 0xE9, 0xD8, 0x51]);
        var outDir = Path.Combine(TempDirectory, "legacy_empty_extract_out");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(Directory.Exists(outDir));
        Assert.Empty(Directory.GetFileSystemEntries(outDir));

        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(0, result.ExtractedFiles);
    }

    #region Conflict Policy Tests

    [Theory]
    [InlineData("--conflict", "skip")]
    [InlineData("-c", "skip")]
    [InlineData("--conflict", "SKIP")]
    public async Task Extract_ConflictPolicySkip_PreservesConflictingFile_AndExitsZero(string flagName, string policy)
    {
        // Arrange
        var archivePath = Path.Combine(TempDirectory, $"conflict_skip_{policy}.zip");
        CreateZipWithEntries(
            archivePath,
            ("existing.txt", "new archive content"u8.ToArray()),
            ("new_file.txt", "brand new content"u8.ToArray()));

        var outDir = Path.Combine(TempDirectory, $"extracted_conflict_skip_{policy}");
        Directory.CreateDirectory(outDir);
        var existingFilePath = Path.Combine(outDir, "existing.txt");
        await File.WriteAllTextAsync(existingFilePath, "original untouched content");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, flagName, policy, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(archivePath), result.ArchivePath);
        Assert.Equal(Path.GetFullPath(outDir), result.DestinationPath);

        // Conflicting file must be preserved
        Assert.Equal("original untouched content", await File.ReadAllTextAsync(existingFilePath));
        // Non-conflicting file must be extracted
        Assert.True(File.Exists(Path.Combine(outDir, "new_file.txt")));
        Assert.Equal("brand new content", await File.ReadAllTextAsync(Path.Combine(outDir, "new_file.txt")));
    }

    [Theory]
    [InlineData("--conflict", "abort")]
    [InlineData("-c", "abort")]
    [InlineData("--conflict", "ABORT")]
    public async Task Extract_ConflictPolicyAbort_HaltsOnCollision_AndExitsOne(string flagName, string policy)
    {
        // Arrange
        var archivePath = Path.Combine(TempDirectory, $"conflict_abort_{policy}.zip");
        CreateZipWithEntries(
            archivePath,
            ("first.txt", "first content"u8.ToArray()),
            ("conflict.txt", "archive conflict content"u8.ToArray()));

        var outDir = Path.Combine(TempDirectory, $"extracted_conflict_abort_{policy}");
        Directory.CreateDirectory(outDir);
        var conflictFilePath = Path.Combine(outDir, "conflict.txt");
        await File.WriteAllTextAsync(conflictFilePath, "pre-existing original content");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, flagName, policy, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);

        // Existing file must remain untouched
        Assert.True(File.Exists(conflictFilePath));
        Assert.Equal("pre-existing original content", await File.ReadAllTextAsync(conflictFilePath));
    }

    [Theory]
    [InlineData("--conflict", "overwrite")]
    [InlineData("-c", "overwrite")]
    [InlineData("--conflict", "OVERWRITE")]
    public async Task Extract_ConflictPolicyOverwrite_OverwritesConflictingFile_AndExitsZero(string flagName, string policy)
    {
        // Arrange
        var archivePath = Path.Combine(TempDirectory, $"conflict_overwrite_{policy}.zip");
        CreateZipWithEntries(
            archivePath,
            ("conflict.txt", "overwritten by archive"u8.ToArray()));

        var outDir = Path.Combine(TempDirectory, $"extracted_conflict_overwrite_{policy}");
        Directory.CreateDirectory(outDir);
        var conflictFilePath = Path.Combine(outDir, "conflict.txt");
        await File.WriteAllTextAsync(conflictFilePath, "old pre-existing content");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, flagName, policy, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(1, result.ExtractedFiles);

        // Conflicting file must have been overwritten
        Assert.Equal("overwritten by archive", await File.ReadAllTextAsync(conflictFilePath));
    }

    [Fact]
    public async Task Extract_ConflictPolicyInvalid_ReturnsExitCode2_AndArgumentErrorJson()
    {
        // Arrange
        var sourceFile = CreateTempFile("conflict_invalid.txt", "valid content");
        var archivePath = Path.Combine(TempDirectory, "conflict_invalid.zrus");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        var outDir = Path.Combine(TempDirectory, "extracted_conflict_invalid");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--conflict", "invalid_policy", "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
        Assert.Contains("Invalid conflict policy 'invalid_policy'. Valid policies: overwrite, skip, abort.", err.Error.Message);
    }

    #region Single-File Zstandard Stream (.zst) Tests

    [Fact]
    public async Task Extract_ZstArchive_JsonMode_ReturnsExitCode0_AndValidJson()
    {
        // Arrange
        var sourceFile = CreateTempFile("data.csv", "id,name,val\n1,Alpha,100\n2,Beta,200\n");
        var archivePath = Path.Combine(TempDirectory, "data.csv.zst");
        var (cExit, _) = await RunCliAsync("compress", sourceFile, "-o", archivePath, "--json");
        Assert.Equal(0, cExit);

        var outDir = Path.Combine(TempDirectory, "extracted_zst_cli");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<ExtractResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(archivePath), result.ArchivePath);
        Assert.Equal(Path.GetFullPath(outDir), result.DestinationPath);
        Assert.Equal(1, result.ExtractedFiles);
        Assert.True(result.TotalBytes > 0);

        var extractedFile = Path.Combine(outDir, "data.csv");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal("id,name,val\n1,Alpha,100\n2,Beta,200\n", await File.ReadAllTextAsync(extractedFile));
    }

    [Fact]
    public async Task Extract_ZstArchive_ConsoleMode_ReturnsExitCode0()
    {
        // Arrange
        var sourceFile = CreateTempFile("log.txt", "Single file log payload");
        var archivePath = Path.Combine(TempDirectory, "log.txt.zst");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        var outDir = Path.Combine(TempDirectory, "extracted_zst_console");

        // Act
        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("Extraction Summary", stdout);
        Assert.True(File.Exists(Path.Combine(outDir, "log.txt")));
    }

    [Fact]
    public async Task Extract_ZstArchive_CorruptedPayload_ReturnsExitCode1_ExecutionError()
    {
        var sourceFile = CreateTempFile("corrupt_test.bin", "sample data to corrupt");
        var archivePath = Path.Combine(TempDirectory, "corrupt_test.bin.zst");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        // Corrupt file
        var bytes = await File.ReadAllBytesAsync(archivePath);
        for (int i = 8; i < bytes.Length; i++) bytes[i] = (byte)~bytes[i];
        await File.WriteAllBytesAsync(archivePath, bytes);

        var outDir = Path.Combine(TempDirectory, "corrupt_zst_out");

        var (exitCode, stdout) = await RunCliAsync("extract", archivePath, "-o", outDir, "--json");
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("Zstandard frame corrupted", err.Error.Message);
        Assert.False(File.Exists(Path.Combine(outDir, "corrupt_test.bin")));
    }

    #endregion

    #endregion

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

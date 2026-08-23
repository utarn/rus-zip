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
    public async Task List_TarZstdArchive_JsonMode_ReturnsExitCode0_AndValidJson()
    {
        // Arrange
        var sourceDir = CreateTempDirectory("list_tarzstd_dir", fileCount: 3);
        var archivePath = Path.Combine(TempDirectory, "test_list.tar.zstd");
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
    }

    [Fact]
    public async Task List_TzstdArchive_JsonMode_ReturnsExitCode0_AndValidJson()
    {
        // Arrange
        var sourceDir = CreateTempDirectory("list_tzstd_dir", fileCount: 2);
        var archivePath = Path.Combine(TempDirectory, "test_list.tzstd");
        await RunCliAsync("compress", sourceDir, archivePath, "--json");

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath, "--json");

        // Assert
        Assert.Equal(0, exitCode);

        var result = ParseJson<ListResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(archivePath), result.ArchivePath);
        Assert.Equal("zrus", result.Format);
        Assert.True(result.TotalEntries >= 2);
        Assert.NotEmpty(result.Entries);
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

    [Fact]
    public async Task List_CorruptedZrus_MidStream_ReturnsExitCode1_AndExecutionErrorJson()
    {
        // F-08: `list` must fail on a checksum-broken archive, not silently succeed.
        var sourceFile = CreateTempFile("midstream_list_cli.bin");
        var payload = new byte[5 * 1024 * 1024];
        new Random(777).NextBytes(payload);
        await File.WriteAllBytesAsync(sourceFile, payload);

        var archivePath = Path.Combine(TempDirectory, "midstream_list_cli.zrus");
        await RunCliAsync("compress", sourceFile, archivePath, "--json");

        var bytes = await File.ReadAllBytesAsync(archivePath);
        bytes[bytes.Length / 2] ^= 0xFF;
        var badPath = Path.Combine(TempDirectory, "midstream_list_cli_bad.zrus");
        await File.WriteAllBytesAsync(badPath, bytes);

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", badPath, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("checksum", err.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_ZipBrokenCentralDirectory_ReturnsExitCode1_AndExecutionErrorJson()
    {
        // F-10 + consistency: the same malformed archive must exit 1 through both list and extract.
        var validZip = TestArchiveFixtures.BuildStoreZip(("a.txt", "AAAAA"), ("b.txt", "BBBBBBBBBB"));
        var broken = TestArchiveFixtures.ZeroCentralDirectoryRegion(validZip);
        var archivePath = Path.Combine(TempDirectory, "broken_cd_list.zip");
        await File.WriteAllBytesAsync(archivePath, broken);

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("central directory", err.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_ZipBrokenEocd_ReturnsExitCode1_AndExecutionErrorJson()
    {
        var validZip = TestArchiveFixtures.BuildStoreZip(("a.txt", "AAAAA"));
        var broken = TestArchiveFixtures.CorruptEocdSignature(validZip);
        var archivePath = Path.Combine(TempDirectory, "broken_eocd_list.zip");
        await File.WriteAllBytesAsync(archivePath, broken);

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath, "--json");

        // Assert
        Assert.Equal(1, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("EXECUTION_ERROR", err.Error.Code);
        Assert.Contains("corrupted or unparseable", err.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_EmptyZip_ReturnsExitCode0_AndZeroEntries()
    {
        // A genuinely empty zip must keep listing (zero entries, exit 0).
        var emptyZip = TestArchiveFixtures.BuildStoreZip();
        var archivePath = Path.Combine(TempDirectory, "empty_list.zip");
        await File.WriteAllBytesAsync(archivePath, emptyZip);

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<ListResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(0, result.TotalEntries);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task List_EmptyZrusArchive_ReturnsExitCode0_AndZeroEntries()
    {
        // F-11: an empty directory compressed to .zrus must list as 0 entries, exit 0 — parity
        // with the .zip path.
        var sourceDir = Path.Combine(TempDirectory, "list_empty_zrus_src");
        Directory.CreateDirectory(sourceDir);
        var archivePath = Path.Combine(TempDirectory, "list_empty.zrus");
        await RunCliAsync("compress", sourceDir, archivePath, "--json");

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<ListResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal("zrus", result.Format);
        Assert.Equal(0, result.TotalEntries);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task List_ArchiveNamedJson_AfterDoubleDash_ReturnsConsoleError_NotJson()
    {
        // F-22: referencing a file literally named "--json" via a "--" separator must produce a
        // console (non-JSON) error. The old global handler scanned raw argv and saw "--json" as
        // the JSON flag, forcing JSON error output for a non-JSON invocation.
        var (exitCode, stdout) = await RunCliAsync("list", "--", "--json");

        // Assert: exit 2 (SOURCE_NOT_FOUND for './--json'), console output, not JSON.
        Assert.Equal(2, exitCode);
        Assert.DoesNotContain("\"success\"", stdout);
        Assert.DoesNotContain("\"error\"", stdout);
        Assert.Contains("Error:", stdout);
        Assert.Contains("--json", stdout);
    }

    [Fact]
    public async Task List_ArchiveNamedJson_DotSlashPrefix_ReturnsConsoleError_NotJson()
    {
        // F-22: the "./--json" form (unambiguous literal filename) must also be a console error.
        var (exitCode, stdout) = await RunCliAsync("list", "./--json");

        Assert.Equal(2, exitCode);
        Assert.DoesNotContain("\"success\"", stdout);
        Assert.DoesNotContain("\"error\"", stdout);
        Assert.Contains("Error:", stdout);
        Assert.Contains("--json", stdout);
    }

    [Fact]
    public async Task List_ArchiveNamedJson_WithActualJsonFlag_ReturnsJsonError()
    {
        // Guard: when the user genuinely passes --json, a missing-argument parse error must still
        // be JSON — the F-22 fix must not swallow legitimate JSON mode.
        var (exitCode, stdout) = await RunCliAsync("list", "--json");

        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
    }

    [Fact]
    public async Task List_LegacyEmptyZrus_ReturnsExitCode0_AndZeroEntries()
    {
        // F-11 read-side compat: the pre-fix 13-byte empty frame (valid zstd, 0 decompressed bytes)
        // must list as 0 entries, exit 0, instead of an integrity error.
        var archivePath = Path.Combine(TempDirectory, "legacy_empty_list.zrus");
        await File.WriteAllBytesAsync(archivePath, [0x28, 0xB5, 0x2F, 0xFD, 0x24, 0x00, 0x01, 0x00, 0x00, 0x99, 0xE9, 0xD8, 0x51]);

        // Act
        var (exitCode, stdout) = await RunCliAsync("list", archivePath, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<ListResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(0, result.TotalEntries);
        Assert.Empty(result.Entries);
    }
}

using RusZip.Cli.Models;
using RusZip.Core.Models;
using Xunit;

namespace RusZip.Cli.Tests;

[Collection("CliTests")]
public sealed class AppendCommandTests : CliTestBase
{
    [Fact]
    public async Task Append_FilesToZrus_ReturnsExitCode0_AndValidJson()
    {
        // Arrange - Create base archive with 1 file
        var file1 = CreateTempFile("initial.txt", "Initial file content");
        var archivePath = Path.Combine(TempDirectory, "archive.zrus");
        var (cExit, _) = await RunCliAsync("compress", file1, archivePath);
        Assert.Equal(0, cExit);

        // Create new file to append
        var file2 = CreateTempFile("appended.txt", "Appended file content");

        // Act - Append
        var (exitCode, stdout) = await RunCliAsync("append", archivePath, file2, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<AppendResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal("zrus", result.Format);
        Assert.Equal(1, result.AddedFiles);
        Assert.Equal(0, result.UpdatedFiles);
        Assert.Equal(1, result.RetainedFiles);
        Assert.Equal(0, result.SkippedFiles);
        Assert.Equal(2, result.TotalFiles);
        Assert.True(result.UncompressedBytes > 0);
        Assert.True(result.CompressedBytes > 0);

        // Verify extraction
        var extractDir = Path.Combine(TempDirectory, "extracted_append");
        var (xExit, _) = await RunCliAsync("extract", archivePath, "-o", extractDir);
        Assert.Equal(0, xExit);
        Assert.True(File.Exists(Path.Combine(extractDir, "initial.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "appended.txt")));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("add")]
    public async Task Append_Aliases_WorkIdentically(string alias)
    {
        // Arrange
        var f1 = CreateTempFile("alias_initial.txt", "Alias test initial");
        var archivePath = Path.Combine(TempDirectory, $"alias_{alias}.zrus");
        await RunCliAsync("compress", f1, archivePath);

        var f2 = CreateTempFile("alias_appended.txt", "Alias test appended");

        // Act
        var (exitCode, stdout) = await RunCliAsync(alias, archivePath, f2, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<AppendResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(2, result.TotalFiles);
    }

    [Fact]
    public async Task Append_CollidingEntry_OverwritesByDefault()
    {
        // Arrange
        var baseDir = Path.Combine(TempDirectory, "collide_base");
        Directory.CreateDirectory(baseDir);
        var f1 = Path.Combine(baseDir, "data.txt");
        await File.WriteAllTextAsync(f1, "Version 1.0");

        var archivePath = Path.Combine(TempDirectory, "collide.zrus");
        await RunCliAsync("compress", f1, archivePath);

        var newDir = Path.Combine(TempDirectory, "collide_new");
        Directory.CreateDirectory(newDir);
        var f2 = Path.Combine(newDir, "data.txt");
        await File.WriteAllTextAsync(f2, "Version 2.0 (Overwritten)");

        // Act
        var (exitCode, stdout) = await RunCliAsync("append", archivePath, f2, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<AppendResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(0, result.AddedFiles);
        Assert.Equal(1, result.UpdatedFiles);
        Assert.Equal(0, result.RetainedFiles);
        Assert.Equal(1, result.TotalFiles);

        var extractDir = Path.Combine(TempDirectory, "extracted_collide");
        await RunCliAsync("extract", archivePath, "-o", extractDir);
        Assert.Equal("Version 2.0 (Overwritten)", await File.ReadAllTextAsync(Path.Combine(extractDir, "data.txt")));
    }

    [Fact]
    public async Task Append_UpdateOnly_WhenOlder_RetainsExistingEntry()
    {
        // Arrange - Base archive with current timestamp
        var baseDir = Path.Combine(TempDirectory, "update_base");
        Directory.CreateDirectory(baseDir);
        var f1 = Path.Combine(baseDir, "doc.txt");
        await File.WriteAllTextAsync(f1, "Original newer doc");
        File.SetLastWriteTimeUtc(f1, DateTime.UtcNow);

        var archivePath = Path.Combine(TempDirectory, "update_only.zrus");
        await RunCliAsync("compress", f1, archivePath);

        // Older incoming file
        var oldDir = Path.Combine(TempDirectory, "update_old");
        Directory.CreateDirectory(oldDir);
        var f2 = Path.Combine(oldDir, "doc.txt");
        await File.WriteAllTextAsync(f2, "Old doc that should be skipped");
        File.SetLastWriteTimeUtc(f2, DateTime.UtcNow.AddHours(-1));

        // Act
        var (exitCode, stdout) = await RunCliAsync("append", archivePath, f2, "-u", "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<AppendResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(0, result.UpdatedFiles);
        Assert.Equal(1, result.RetainedFiles);
        Assert.Equal(1, result.SkippedFiles);
        Assert.Equal(1, result.TotalFiles);

        var extractDir = Path.Combine(TempDirectory, "extracted_update_old");
        await RunCliAsync("extract", archivePath, "-o", extractDir);
        Assert.Equal("Original newer doc", await File.ReadAllTextAsync(Path.Combine(extractDir, "doc.txt")));
    }

    [Fact]
    public async Task CompressCommand_WithAppendFlag_AppendsToTargetArchive()
    {
        // Arrange
        var f1 = CreateTempFile("c_base.txt", "Compress base");
        var archivePath = Path.Combine(TempDirectory, "compress_append.zrus");
        await RunCliAsync("compress", f1, "-o", archivePath);

        var f2 = CreateTempFile("c_appended.txt", "Compress appended");

        // Act - use `compress ... -o archive.zrus --append`
        var (exitCode, stdout) = await RunCliAsync("compress", f2, "-o", archivePath, "--append", "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(2, result.TotalFiles);

        var extractDir = Path.Combine(TempDirectory, "extracted_c_append");
        await RunCliAsync("extract", archivePath, "-o", extractDir);
        Assert.True(File.Exists(Path.Combine(extractDir, "c_base.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "c_appended.txt")));
    }

    [Fact]
    public async Task CompressCommand_WithAppendAndUpdateOnlyFlags_HonorsTimestamps()
    {
        // Arrange
        var f1 = CreateTempFile("c_u_base.txt", "Base doc");
        File.SetLastWriteTimeUtc(f1, DateTime.UtcNow);
        var archivePath = Path.Combine(TempDirectory, "compress_append_u.zrus");
        await RunCliAsync("compress", f1, "-o", archivePath);

        var oldDir = Path.Combine(TempDirectory, "c_u_old");
        Directory.CreateDirectory(oldDir);
        var f2 = Path.Combine(oldDir, "c_u_base.txt");
        await File.WriteAllTextAsync(f2, "Older attempt");
        File.SetLastWriteTimeUtc(f2, DateTime.UtcNow.AddHours(-2));

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", f2, "-o", archivePath, "-a", "-u", "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalFiles);

        var extractDir = Path.Combine(TempDirectory, "extracted_c_u");
        await RunCliAsync("extract", archivePath, "-o", extractDir);
        Assert.Equal("Base doc", await File.ReadAllTextAsync(Path.Combine(extractDir, "c_u_base.txt")));
    }

    [Fact]
    public async Task Append_NonExistentArchive_ReturnsExitCode2_SourceNotFound()
    {
        var f1 = CreateTempFile("file.txt", "content");
        var missingArchive = Path.Combine(TempDirectory, "does_not_exist.zrus");

        var (exitCode, stdout) = await RunCliAsync("append", missingArchive, f1, "--json");

        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("SOURCE_NOT_FOUND", err.Error.Code);
    }

    [Fact]
    public async Task Append_NonExistentSource_ReturnsExitCode2_SourceNotFound()
    {
        var f1 = CreateTempFile("exists.txt", "content");
        var archivePath = Path.Combine(TempDirectory, "archive_ok.zrus");
        await RunCliAsync("compress", f1, archivePath);

        var missingSource = Path.Combine(TempDirectory, "missing_source_file.txt");

        var (exitCode, stdout) = await RunCliAsync("append", archivePath, missingSource, "--json");

        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("SOURCE_NOT_FOUND", err.Error.Code);
    }

    [Fact]
    public async Task Append_UnsupportedFormat_ReturnsExitCode2_UnsupportedFormat()
    {
        var f1 = CreateTempFile("file.txt", "content");
        var zipPath = Path.Combine(TempDirectory, "test.zip");
        await RunCliAsync("compress", f1, zipPath);

        var f2 = CreateTempFile("file2.txt", "content2");

        var (exitCode, stdout) = await RunCliAsync("append", zipPath, f2, "--json");

        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("UNSUPPORTED_FORMAT", err.Error.Code);
    }

    [Fact]
    public async Task Append_InvalidLevel_ReturnsExitCode2_ArgumentError()
    {
        var f1 = CreateTempFile("f1.txt", "content");
        var archivePath = Path.Combine(TempDirectory, "level_cli.zrus");
        await RunCliAsync("compress", f1, archivePath);

        var f2 = CreateTempFile("f2.txt", "content2");

        var (exitCode, stdout) = await RunCliAsync("append", archivePath, f2, "-l", "99", "--json");

        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
    }

    [Fact]
    public async Task Append_Directory_ConsoleOutput_RendersSummary()
    {
        var f1 = CreateTempFile("f1.txt", "content");
        var archivePath = Path.Combine(TempDirectory, "console_test.zrus");
        await RunCliAsync("compress", f1, archivePath);

        var subDir = CreateTempDirectory("sub_append", fileCount: 2);

        var (exitCode, stdout) = await RunCliAsync("append", archivePath, subDir);

        Assert.Equal(0, exitCode);
        Assert.Contains("Append Summary", stdout);
        Assert.Contains("Added Files", stdout);
        Assert.Contains("Retained Files", stdout);
        Assert.Contains("Total Files", stdout);
    }
}

using System.Text.Json;
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

    [Theory]
    [InlineData("test.rar")]
    [InlineData("test.7z")]
    [InlineData("test.gz")]
    [InlineData("test.tar.gz")]
    [InlineData("test.tgz")]
    public async Task Append_UnsupportedFormat_JsonMode_ReturnsExitCode2_UnsupportedFormat(string archiveFilename)
    {
        var dummyPath = Path.Combine(TempDirectory, archiveFilename);
        await File.WriteAllTextAsync(dummyPath, "dummy unsupported archive content");

        var sourceFile = CreateTempFile("file_to_append.txt", "content");

        var (exitCode, stdout) = await RunCliAsync("append", dummyPath, sourceFile, "--json");

        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("UNSUPPORTED_FORMAT", err.Error.Code);
        Assert.Contains("not supported", err.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("test_console.rar")]
    [InlineData("test_console.7z")]
    [InlineData("test_console.gz")]
    [InlineData("test_console.tar.gz")]
    [InlineData("test_console.tgz")]
    public async Task Append_UnsupportedFormat_ConsoleMode_ReturnsExitCode2_UnsupportedFormat(string archiveFilename)
    {
        var dummyPath = Path.Combine(TempDirectory, archiveFilename);
        await File.WriteAllTextAsync(dummyPath, "dummy unsupported archive content");

        var sourceFile = CreateTempFile("file_console_append.txt", "content");

        var (exitCode, stdout) = await RunCliAsync("append", dummyPath, sourceFile);

        Assert.Equal(2, exitCode);
        Assert.Contains("Error:", stdout);
        Assert.Contains("not supported", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("test_compress_append.rar")]
    [InlineData("test_compress_append.7z")]
    [InlineData("test_compress_append.gz")]
    [InlineData("test_compress_append.tar.gz")]
    public async Task Compress_WithAppendFlag_UnsupportedFormat_ReturnsExitCode2_UnsupportedFormat(string archiveFilename)
    {
        var dummyPath = Path.Combine(TempDirectory, archiveFilename);
        await File.WriteAllTextAsync(dummyPath, "dummy unsupported archive content");

        var sourceFile = CreateTempFile("file_c_append.txt", "content");

        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, "-o", dummyPath, "--append", "--json");

        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("UNSUPPORTED_FORMAT", err.Error.Code);
        Assert.Contains("not supported", err.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Append_FilesToZip_ReturnsExitCode0_AndValidJson()
    {
        // Arrange - Create base archive with 1 file
        var file1 = CreateTempFile("initial_zip.txt", "Initial zip content");
        var archivePath = Path.Combine(TempDirectory, "archive.zip");
        var (cExit, _) = await RunCliAsync("compress", file1, archivePath);
        Assert.Equal(0, cExit);

        // Create new file to append
        var file2 = CreateTempFile("appended_zip.txt", "Appended zip content");

        // Act - Append
        var (exitCode, stdout) = await RunCliAsync("append", archivePath, file2, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<AppendResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal("zip", result.Format);
        Assert.Equal(1, result.AddedFiles);
        Assert.Equal(0, result.UpdatedFiles);
        Assert.Equal(1, result.RetainedFiles);
        Assert.Equal(0, result.SkippedFiles);
        Assert.Equal(2, result.TotalFiles);
        Assert.True(result.UncompressedBytes > 0);
        Assert.True(result.CompressedBytes > 0);

        // Verify extraction
        var extractDir = Path.Combine(TempDirectory, "extracted_zip_append");
        var (xExit, _) = await RunCliAsync("extract", archivePath, "-o", extractDir);
        Assert.Equal(0, xExit);
        Assert.True(File.Exists(Path.Combine(extractDir, "initial_zip.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "appended_zip.txt")));
    }

    [Fact]
    public async Task Append_Zip_CollidingEntry_OverwritesByDefault()
    {
        // Arrange
        var baseDir = Path.Combine(TempDirectory, "collide_zip_base");
        Directory.CreateDirectory(baseDir);
        var f1 = Path.Combine(baseDir, "data.txt");
        await File.WriteAllTextAsync(f1, "Zip Version 1.0");

        var archivePath = Path.Combine(TempDirectory, "collide.zip");
        await RunCliAsync("compress", f1, archivePath);

        var newDir = Path.Combine(TempDirectory, "collide_zip_new");
        Directory.CreateDirectory(newDir);
        var f2 = Path.Combine(newDir, "data.txt");
        await File.WriteAllTextAsync(f2, "Zip Version 2.0 (Overwritten)");

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

        var extractDir = Path.Combine(TempDirectory, "extracted_zip_collide");
        await RunCliAsync("extract", archivePath, "-o", extractDir);
        Assert.Equal("Zip Version 2.0 (Overwritten)", await File.ReadAllTextAsync(Path.Combine(extractDir, "data.txt")));
    }

    [Fact]
    public async Task Append_Zip_UpdateOnly_WhenOlder_RetainsExistingEntry()
    {
        // Arrange - Base archive with current timestamp
        var baseDir = Path.Combine(TempDirectory, "update_zip_base");
        Directory.CreateDirectory(baseDir);
        var f1 = Path.Combine(baseDir, "doc.txt");
        await File.WriteAllTextAsync(f1, "Original newer zip doc");
        File.SetLastWriteTimeUtc(f1, DateTime.UtcNow);

        var archivePath = Path.Combine(TempDirectory, "update_only.zip");
        await RunCliAsync("compress", f1, archivePath);

        // Older incoming file
        var oldDir = Path.Combine(TempDirectory, "update_zip_old");
        Directory.CreateDirectory(oldDir);
        var f2 = Path.Combine(oldDir, "doc.txt");
        await File.WriteAllTextAsync(f2, "Old zip doc that should be skipped");
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

        var extractDir = Path.Combine(TempDirectory, "extracted_zip_update_old");
        await RunCliAsync("extract", archivePath, "-o", extractDir);
        Assert.Equal("Original newer zip doc", await File.ReadAllTextAsync(Path.Combine(extractDir, "doc.txt")));
    }

    [Fact]
    public async Task Append_Zip_UpdateOnly_WhenNewer_OverwritesExistingEntry()
    {
        // Arrange - Base archive with older timestamp
        var baseDir = Path.Combine(TempDirectory, "update_zip_newer_base");
        Directory.CreateDirectory(baseDir);
        var f1 = Path.Combine(baseDir, "doc.txt");
        await File.WriteAllTextAsync(f1, "Original older zip doc");
        File.SetLastWriteTimeUtc(f1, DateTime.UtcNow.AddHours(-2));

        var archivePath = Path.Combine(TempDirectory, "update_only_newer.zip");
        await RunCliAsync("compress", f1, archivePath);

        // Newer incoming file
        var newDir = Path.Combine(TempDirectory, "update_zip_newer_in");
        Directory.CreateDirectory(newDir);
        var f2 = Path.Combine(newDir, "doc.txt");
        await File.WriteAllTextAsync(f2, "New zip doc that should replace old");
        File.SetLastWriteTimeUtc(f2, DateTime.UtcNow);

        // Act
        var (exitCode, stdout) = await RunCliAsync("append", archivePath, f2, "-u", "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<AppendResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(1, result.UpdatedFiles);
        Assert.Equal(0, result.RetainedFiles);
        Assert.Equal(0, result.SkippedFiles);
        Assert.Equal(1, result.TotalFiles);

        var extractDir = Path.Combine(TempDirectory, "extracted_zip_update_newer");
        await RunCliAsync("extract", archivePath, "-o", extractDir);
        Assert.Equal("New zip doc that should replace old", await File.ReadAllTextAsync(Path.Combine(extractDir, "doc.txt")));
    }

    [Fact]
    public async Task CompressCommand_WithAppendFlag_Zip_AppendsToTargetArchive()
    {
        // Arrange
        var f1 = CreateTempFile("c_zip_base.txt", "Compress zip base");
        var archivePath = Path.Combine(TempDirectory, "compress_append.zip");
        await RunCliAsync("compress", f1, "-o", archivePath);

        var f2 = CreateTempFile("c_zip_appended.txt", "Compress zip appended");

        // Act - use `compress ... -o archive.zip --append`
        var (exitCode, stdout) = await RunCliAsync("compress", f2, "-o", archivePath, "--append", "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal("zip", result.Format);
        Assert.Equal(2, result.TotalFiles);

        var extractDir = Path.Combine(TempDirectory, "extracted_c_zip_append");
        await RunCliAsync("extract", archivePath, "-o", extractDir);
        Assert.True(File.Exists(Path.Combine(extractDir, "c_zip_base.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "c_zip_appended.txt")));
    }

    [Fact]
    public async Task CompressCommand_WithAppendAndUpdateOnlyFlags_Zip_HonorsTimestamps()
    {
        // Arrange
        var f1 = CreateTempFile("c_u_zip_base.txt", "Base zip doc");
        File.SetLastWriteTimeUtc(f1, DateTime.UtcNow);
        var archivePath = Path.Combine(TempDirectory, "compress_append_u.zip");
        await RunCliAsync("compress", f1, "-o", archivePath);

        var oldDir = Path.Combine(TempDirectory, "c_u_zip_old");
        Directory.CreateDirectory(oldDir);
        var f2 = Path.Combine(oldDir, "c_u_zip_base.txt");
        await File.WriteAllTextAsync(f2, "Older zip attempt");
        File.SetLastWriteTimeUtc(f2, DateTime.UtcNow.AddHours(-2));

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", f2, "-o", archivePath, "-a", "-u", "--json");

        // Assert
        Assert.Equal(0, exitCode);
        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal("zip", result.Format);
        Assert.Equal(1, result.TotalFiles);

        var extractDir = Path.Combine(TempDirectory, "extracted_c_u_zip");
        await RunCliAsync("extract", archivePath, "-o", extractDir);
        Assert.Equal("Base zip doc", await File.ReadAllTextAsync(Path.Combine(extractDir, "c_u_zip_base.txt")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(-1)]
    [InlineData(100)]
    public async Task Append_Zrus_InvalidLevel_ReturnsExitCode2_ArgumentError(int invalidLevel)
    {
        var f1 = CreateTempFile("f1.txt", "content");
        var archivePath = Path.Combine(TempDirectory, $"level_cli_{invalidLevel}.zrus");
        await RunCliAsync("compress", f1, archivePath);

        var f2 = CreateTempFile("f2.txt", "content2");

        var (exitCode, stdout) = await RunCliAsync("append", archivePath, f2, "-l", invalidLevel.ToString(), "--json");

        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
        Assert.Contains("Compression level", err.Error.Message);
        Assert.Contains(".zrus", err.Error.Message);
        Assert.Contains("1-22", err.Error.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(22)]
    public async Task Append_Zip_InvalidLevel_ReturnsExitCode2_ArgumentError(int invalidLevel)
    {
        var f1 = CreateTempFile("f1_zip.txt", "content");
        var archivePath = Path.Combine(TempDirectory, $"level_cli_{invalidLevel}.zip");
        await RunCliAsync("compress", f1, archivePath);

        var f2 = CreateTempFile("f2_zip.txt", "content2");

        var (exitCode, stdout) = await RunCliAsync("append", archivePath, f2, "-l", invalidLevel.ToString(), "--json");

        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
        Assert.Contains("Compression level", err.Error.Message);
        Assert.Contains(".zip", err.Error.Message);
        Assert.Contains("0-9", err.Error.Message);
    }

    [Fact]
    public async Task Append_JsonOutput_MatchesAllRequiredSchemaProperties()
    {
        // Arrange
        var file1 = CreateTempFile("schema1.txt", "Schema test file 1");
        var archivePath = Path.Combine(TempDirectory, "schema_test.zrus");
        var (cExit, _) = await RunCliAsync("compress", file1, archivePath);
        Assert.Equal(0, cExit);

        var file2 = CreateTempFile("schema2.txt", "Schema test file 2");

        // Act
        var (exitCode, stdout) = await RunCliAsync("append", archivePath, file2, "--json");

        // Assert
        Assert.Equal(0, exitCode);

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        // Verify root structure & camelCase property names
        Assert.True(root.TryGetProperty("success", out var successProp) && successProp.ValueKind == JsonValueKind.True);
        Assert.True(root.TryGetProperty("archivePath", out var archivePathProp) && archivePathProp.ValueKind == JsonValueKind.String);
        Assert.True(root.TryGetProperty("format", out var formatProp) && formatProp.ValueKind == JsonValueKind.String);
        Assert.True(root.TryGetProperty("addedFiles", out var addedProp) && addedProp.ValueKind == JsonValueKind.Number);
        Assert.True(root.TryGetProperty("updatedFiles", out var updatedProp) && updatedProp.ValueKind == JsonValueKind.Number);
        Assert.True(root.TryGetProperty("retainedFiles", out var retainedProp) && retainedProp.ValueKind == JsonValueKind.Number);
        Assert.True(root.TryGetProperty("skippedFiles", out var skippedProp) && skippedProp.ValueKind == JsonValueKind.Number);
        Assert.True(root.TryGetProperty("totalFiles", out var totalProp) && totalProp.ValueKind == JsonValueKind.Number);
        Assert.True(root.TryGetProperty("uncompressedBytes", out var uncompressedProp) && uncompressedProp.ValueKind == JsonValueKind.Number);
        Assert.True(root.TryGetProperty("compressedBytes", out var compressedProp) && compressedProp.ValueKind == JsonValueKind.Number);
        Assert.True(root.TryGetProperty("compressionRatio", out var ratioProp) && ratioProp.ValueKind == JsonValueKind.Number);
        Assert.True(root.TryGetProperty("elapsedMilliseconds", out var elapsedProp) && elapsedProp.ValueKind == JsonValueKind.Number);

        Assert.Equal("zrus", formatProp.GetString());
        Assert.Equal(1, addedProp.GetInt32());
        Assert.Equal(0, updatedProp.GetInt32());
        Assert.Equal(1, retainedProp.GetInt32());
        Assert.Equal(0, skippedProp.GetInt32());
        Assert.Equal(2, totalProp.GetInt32());
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
        Assert.Contains("Archive Path", stdout);
        Assert.Contains("Added Files", stdout);
        Assert.Contains("Updated Files", stdout);
        Assert.Contains("Retained Files", stdout);
        Assert.Contains("Skipped Files", stdout);
        Assert.Contains("Total Files", stdout);
        Assert.Contains("Uncompressed Size", stdout);
        Assert.Contains("Compressed Size", stdout);
        Assert.Contains("Ratio", stdout);
        Assert.Contains("Time Elapsed", stdout);
    }

    [Fact]
    public async Task Append_Zip_Directory_ConsoleOutput_RendersSummary()
    {
        var f1 = CreateTempFile("f1_zip.txt", "content");
        var archivePath = Path.Combine(TempDirectory, "console_test.zip");
        await RunCliAsync("compress", f1, archivePath);

        var subDir = CreateTempDirectory("sub_zip_append", fileCount: 2);

        var (exitCode, stdout) = await RunCliAsync("append", archivePath, subDir);

        Assert.Equal(0, exitCode);
        Assert.Contains("Append Summary", stdout);
        Assert.Contains("Archive Path", stdout);
        Assert.Contains("Added Files", stdout);
        Assert.Contains("Updated Files", stdout);
        Assert.Contains("Retained Files", stdout);
        Assert.Contains("Skipped Files", stdout);
        Assert.Contains("Total Files", stdout);
        Assert.Contains("Uncompressed Size", stdout);
        Assert.Contains("Compressed Size", stdout);
        Assert.Contains("Ratio", stdout);
        Assert.Contains("Time Elapsed", stdout);
    }
}

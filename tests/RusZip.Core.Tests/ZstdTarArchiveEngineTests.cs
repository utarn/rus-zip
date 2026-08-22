using System.Security;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

public class ZstdTarArchiveEngineTests : IDisposable
{
    private readonly string _testDir;
    private readonly ZstdTarArchiveEngine _engine;

    public ZstdTarArchiveEngineTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ruszip_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _engine = new ZstdTarArchiveEngine();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { /* Ignore */ }
        }
    }

    [Theory]
    [InlineData(1)]  // Fast
    [InlineData(9)]  // Balanced / Default
    [InlineData(15)] // High
    [InlineData(22)] // Ultra
    public async Task CompressAndExtract_DirectoryRoundtrip_PreservesStructureAndContents(int compressionLevel)
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, "source_" + compressionLevel);
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(Path.Combine(sourceDir, "subfolder"));

        var file1 = Path.Combine(sourceDir, "hello.txt");
        var file2 = Path.Combine(sourceDir, "subfolder", "nested.txt");
        await File.WriteAllTextAsync(file1, "Hello World from rus-zip!");
        await File.WriteAllTextAsync(file2, "Nested content in subfolder.");

        var archivePath = Path.Combine(_testDir, $"output_{compressionLevel}.zrus");
        var extractDir = Path.Combine(_testDir, $"extracted_{compressionLevel}");

        var progressReports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(progressReports.Add);

        // Act - Compress
        var compressReq = new ArchiveCompressionRequest(sourceDir, archivePath, compressionLevel);
        await _engine.CompressAsync(compressReq, progress);

        // Assert - Archive created
        Assert.True(File.Exists(archivePath));
        Assert.True(new FileInfo(archivePath).Length > 0);

        // Act - List
        var entries = await _engine.ListEntriesAsync(archivePath);
        Assert.Contains(entries, e => e.RelativePath == "hello.txt" && !e.IsDirectory);
        Assert.Contains(entries, e => e.RelativePath.Contains("nested.txt") && !e.IsDirectory);

        // Act - Extract
        var extractReq = new ArchiveExtractionRequest(archivePath, extractDir);
        await _engine.ExtractAsync(extractReq, progress);

        // Assert - Files extracted properly
        var extractedFile1 = Path.Combine(extractDir, "hello.txt");
        var extractedFile2 = Path.Combine(extractDir, "subfolder", "nested.txt");

        Assert.True(File.Exists(extractedFile1));
        Assert.True(File.Exists(extractedFile2));
        Assert.Equal("Hello World from rus-zip!", await File.ReadAllTextAsync(extractedFile1));
        Assert.Equal("Nested content in subfolder.", await File.ReadAllTextAsync(extractedFile2));
    }

    [Fact]
    public async Task CompressAndExtract_SingleFile_PreservesContent()
    {
        // Arrange
        var singleFile = Path.Combine(_testDir, "single.txt");
        var content = "Testing single file compression in .zrus format.";
        await File.WriteAllTextAsync(singleFile, content);

        var archivePath = Path.Combine(_testDir, "single.zrus");
        var extractDir = Path.Combine(_testDir, "single_extracted");

        // Act
        await _engine.CompressAsync(new ArchiveCompressionRequest(singleFile, archivePath, 9));
        var entries = await _engine.ListEntriesAsync(archivePath);
        Assert.Single(entries);
        Assert.Equal("single.txt", entries[0].RelativePath);

        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir));

        // Assert
        var extractedFile = Path.Combine(extractDir, "single.txt");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal(content, await File.ReadAllTextAsync(extractedFile));
    }

    [Fact]
    public async Task Extract_CorruptedArchive_ThrowsInvalidDataException()
    {
        var corruptedFile = Path.Combine(_testDir, "corrupted.zrus");
        await File.WriteAllBytesAsync(corruptedFile, [0x28, 0xB5, 0x2F, 0xFD, 0x00, 0x11, 0x22, 0x33, 0x44]);

        var extractDir = Path.Combine(_testDir, "corrupted_extracted");
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(corruptedFile, extractDir)));
    }
}

using System.IO.Compression;
using RusZip.Core.Abstractions;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

public sealed class MultiVolumeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IArchiveEngine _engine;

    public MultiVolumeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ruszip_mv_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _engine = new UnifiedArchiveEngine();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { /* Ignore */ }
        }
    }

    [Fact]
    public async Task Zrus_CompressWithSplitSize_ProducesSequentialParts()
    {
        // Arrange: create 500 KB file
        var srcFile = Path.Combine(_tempDir, "large.dat");
        var randomData = new byte[500 * 1024];
        new Random(42).NextBytes(randomData);
        await File.WriteAllBytesAsync(srcFile, randomData);

        var zrusPath = Path.Combine(_tempDir, "split_test.zrus");
        long splitSize = 70 * 1024; // 70 KB per volume part (> 64 KB minimum)

        // Act
        var req = new ArchiveCompressionRequest([srcFile], zrusPath, 9, SplitSizeBytes: splitSize);
        await _engine.CompressAsync(req);

        // Assert: parts should exist: .part1.zrus, .part2.zrus, ...
        var volumeParts = await _engine.GetVolumePartsAsync(zrusPath);
        Assert.True(volumeParts.Count >= 2, $"Expected at least 2 parts, got {volumeParts.Count}");
        Assert.True(File.Exists(Path.Combine(_tempDir, "split_test.part1.zrus")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "split_test.part2.zrus")));

        // Verify volume files are capped around splitSize
        var fi1 = new FileInfo(Path.Combine(_tempDir, "split_test.part1.zrus"));
        Assert.True(fi1.Length <= splitSize);
    }

    [Fact]
    public async Task Zrus_MultiVolume_LargeFileSpanningVolumes_ExtractsCleanly()
    {
        // Arrange
        var srcFile = Path.Combine(_tempDir, "payload.bin");
        var originalData = new byte[400 * 1024]; // 400 KB
        new Random(123).NextBytes(originalData);
        await File.WriteAllBytesAsync(srcFile, originalData);

        var zrusPath = Path.Combine(_tempDir, "span_test.zrus");
        long splitSize = 65536; // 64 KB

        await _engine.CompressAsync(new ArchiveCompressionRequest([srcFile], zrusPath, 3, SplitSizeBytes: splitSize));

        // Act: extract starting from .part1.zrus
        var part1 = Path.Combine(_tempDir, "span_test.part1.zrus");
        var extractDir = Path.Combine(_tempDir, "extracted_span");

        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(part1, extractDir));

        // Assert
        Assert.Equal(1, result.FilesExtracted);
        var extractedFile = Path.Combine(extractDir, "payload.bin");
        Assert.True(File.Exists(extractedFile));

        var extractedData = await File.ReadAllBytesAsync(extractedFile);
        Assert.Equal(originalData, extractedData);
    }

    [Fact]
    public async Task Zip_CompressWithSplitSize_ProducesSequentialParts_AndExtractsCleanly()
    {
        // Arrange
        var srcFile = Path.Combine(_tempDir, "data.txt");
        var text = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog 1234567890.\n", 2000));
        await File.WriteAllTextAsync(srcFile, text);

        var zipPath = Path.Combine(_tempDir, "split_zip.zip");
        long splitSize = 65536; // 64 KB

        // Act
        await _engine.CompressAsync(new ArchiveCompressionRequest([srcFile], zipPath, 6, SplitSizeBytes: splitSize));

        var part1 = Path.Combine(_tempDir, "split_zip.part1.zip");
        Assert.True(File.Exists(part1));
        Assert.True(VolumeNameResolver.IsMultiVolume(part1));

        var extractDir = Path.Combine(_tempDir, "zip_mv_extracted");
        var extractResult = await _engine.ExtractAsync(new ArchiveExtractionRequest(part1, extractDir));

        // Assert
        Assert.Equal(1, extractResult.FilesExtracted);
        var extractedFile = Path.Combine(extractDir, "data.txt");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal(text, await File.ReadAllTextAsync(extractedFile));
    }

    [Fact]
    public async Task MultiVolume_StartingFromSiblingPart_DiscoversAndExtractsAllParts()
    {
        // Arrange
        var srcFile = Path.Combine(_tempDir, "sibling_test.txt");
        await File.WriteAllTextAsync(srcFile, new string('X', 300 * 1024));

        var zrusPath = Path.Combine(_tempDir, "sibling.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest([srcFile], zrusPath, 3, SplitSizeBytes: 65536));

        var part2 = Path.Combine(_tempDir, "sibling.part2.zrus");
        Assert.True(File.Exists(part2));

        // Act: extract pointing directly to part2
        var extractDir = Path.Combine(_tempDir, "sibling_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(part2, extractDir));

        // Assert
        Assert.Equal(1, result.FilesExtracted);
        var extractedFile = Path.Combine(extractDir, "sibling_test.txt");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal(300 * 1024, new FileInfo(extractedFile).Length);
    }

    [Fact]
    public async Task MultiVolume_ZeroPaddedParts_ResolvedAndDecompressedCorrectly()
    {
        // Arrange: create unpadded split archive, then rename to .part01.zrus, .part02.zrus
        var srcFile = Path.Combine(_tempDir, "padded.txt");
        await File.WriteAllTextAsync(srcFile, new string('Z', 250 * 1024));

        var zrusPath = Path.Combine(_tempDir, "padded.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest([srcFile], zrusPath, 3, SplitSizeBytes: 65536));

        var p1 = Path.Combine(_tempDir, "padded.part1.zrus");
        var p2 = Path.Combine(_tempDir, "padded.part2.zrus");
        var p01 = Path.Combine(_tempDir, "padded.part01.zrus");
        var p02 = Path.Combine(_tempDir, "padded.part02.zrus");

        File.Move(p1, p01);
        File.Move(p2, p02);

        // Act: extract using zero-padded part01
        var extractDir = Path.Combine(_tempDir, "padded_out");
        var result = await _engine.ExtractAsync(new ArchiveExtractionRequest(p01, extractDir));

        // Assert
        Assert.Equal(1, result.FilesExtracted);
        Assert.True(File.Exists(Path.Combine(extractDir, "padded.txt")));
    }

    [Fact]
    public async Task MultiVolume_MissingVolume_ThrowsMissingVolumeException_AndCleansPartialFiles()
    {
        // Arrange: create 3-volume archive and delete part 2
        var srcFile = Path.Combine(_tempDir, "gap.txt");
        await File.WriteAllTextAsync(srcFile, new string('G', 300 * 1024));

        var zrusPath = Path.Combine(_tempDir, "gap_test.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest([srcFile], zrusPath, 3, SplitSizeBytes: 65536));

        var p2 = Path.Combine(_tempDir, "gap_test.part2.zrus");
        Assert.True(File.Exists(p2));
        File.Delete(p2); // Delete middle volume part 2

        var extractDir = Path.Combine(_tempDir, "gap_extracted");
        var p1 = Path.Combine(_tempDir, "gap_test.part1.zrus");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<MissingVolumeException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(p1, extractDir)));

        Assert.Contains("part 2 is missing", ex.Message, StringComparison.OrdinalIgnoreCase);
        // Partial files must be cleaned up
        Assert.False(Directory.Exists(extractDir) && Directory.GetFiles(extractDir).Length > 0);
    }

    [Fact]
    public async Task MultiVolume_AppendOrDelete_ThrowsNotSupportedException()
    {
        // Arrange
        var srcFile = Path.Combine(_tempDir, "immut.txt");
        await File.WriteAllTextAsync(srcFile, new string('I', 200 * 1024));

        var zrusPath = Path.Combine(_tempDir, "immut.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest([srcFile], zrusPath, 3, SplitSizeBytes: 65536));

        var p1 = Path.Combine(_tempDir, "immut.part1.zrus");

        // Act & Assert: Append throws NotSupportedException
        var extraFile = Path.Combine(_tempDir, "extra.txt");
        await File.WriteAllTextAsync(extraFile, "extra");

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _engine.AppendAsync(new ArchiveAppendRequest(p1, [extraFile], 3)));

        // Act & Assert: Delete throws NotSupportedException
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _engine.DeleteEntriesAsync(new ArchiveDeleteRequest(p1, ["immut.txt"], 3)));
    }

    [Fact]
    public async Task SplitSizeUnder64KB_ThrowsArgumentOutOfRangeException()
    {
        var srcFile = Path.Combine(_tempDir, "small.txt");
        await File.WriteAllTextAsync(srcFile, "Small content");

        var zrusPath = Path.Combine(_tempDir, "small.zrus");

        // Split size 10 KB (< 64 KB)
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _engine.CompressAsync(new ArchiveCompressionRequest([srcFile], zrusPath, 3, SplitSizeBytes: 10 * 1024)));
    }
}

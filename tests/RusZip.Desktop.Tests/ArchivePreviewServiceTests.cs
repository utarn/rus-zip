using System.Diagnostics;
using RusZip.Core.Abstractions;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using RusZip.Desktop.Services;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class ArchivePreviewServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public ArchivePreviewServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ruszip-preview-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { /* Ignored */ }
        }
    }

    [Fact]
    public async Task ExtractPreviewAsync_ExtractsFileToIsolatedTempDirectory()
    {
        var sampleDir = Path.Combine(_tempDirectory, "sample");
        Directory.CreateDirectory(sampleDir);
        var originalFile = Path.Combine(sampleDir, "document.txt");
        await File.WriteAllTextAsync(originalFile, "Sample content for preview");

        var zrusPath = Path.Combine(_tempDirectory, "archive.zrus");
        var engine = new UnifiedArchiveEngine();
        await engine.CompressAsync(new ArchiveCompressionRequest([originalFile], zrusPath, 3));

        var service = new ArchivePreviewService(engine);
        try
        {
            var extractedPath = await service.ExtractPreviewAsync(zrusPath, "document.txt");

            Assert.True(File.Exists(extractedPath));
            Assert.Contains("rus-zip-preview", extractedPath);
            Assert.Equal("Sample content for preview", await File.ReadAllTextAsync(extractedPath));
            Assert.Single(service.ActivePreviewDirectories);
        }
        finally
        {
            await service.CleanupAsync();
        }
    }

    [Fact]
    public async Task PreviewEntryAsync_LaunchesConfiguredProcessViewer()
    {
        var sampleDir = Path.Combine(_tempDirectory, "sample_preview");
        Directory.CreateDirectory(sampleDir);
        var originalFile = Path.Combine(sampleDir, "image.png");
        await File.WriteAllTextAsync(originalFile, "image-bytes");

        var zrusPath = Path.Combine(_tempDirectory, "test.zrus");
        var engine = new UnifiedArchiveEngine();
        await engine.CompressAsync(new ArchiveCompressionRequest([originalFile], zrusPath, 3));

        string? launchedFile = null;
        var service = new ArchivePreviewService(engine, filePath =>
        {
            launchedFile = filePath;
            return null;
        });

        try
        {
            await service.PreviewEntryAsync(zrusPath, "image.png");

            Assert.NotNull(launchedFile);
            Assert.True(File.Exists(launchedFile));
            Assert.EndsWith("image.png", launchedFile);
        }
        finally
        {
            await service.CleanupAsync();
        }
    }

    [Fact]
    public async Task CleanupAsync_DeletesAllTrackedDirectories()
    {
        var sampleDir = Path.Combine(_tempDirectory, "sample_cleanup");
        Directory.CreateDirectory(sampleDir);
        var originalFile = Path.Combine(sampleDir, "note.txt");
        await File.WriteAllTextAsync(originalFile, "cleanup note");

        var zrusPath = Path.Combine(_tempDirectory, "test_clean.zrus");
        var engine = new UnifiedArchiveEngine();
        await engine.CompressAsync(new ArchiveCompressionRequest([originalFile], zrusPath, 3));

        var service = new ArchivePreviewService(engine);
        var extractedPath = await service.ExtractPreviewAsync(zrusPath, "note.txt");
        var dirPath = Path.GetDirectoryName(extractedPath)!;

        // Parent or direct folder in active directories
        var trackedDirs = service.ActivePreviewDirectories.ToList();
        Assert.NotEmpty(trackedDirs);
        Assert.All(trackedDirs, d => Assert.True(Directory.Exists(d)));

        await service.CleanupAsync();

        Assert.Empty(service.ActivePreviewDirectories);
        Assert.All(trackedDirs, d => Assert.False(Directory.Exists(d)));
    }
}

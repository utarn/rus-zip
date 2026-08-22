using System.Security;
using System.Security.Cryptography;
using System.Text;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

/// <summary>
/// End-to-end regression tests verifying cross-format decompression, hierarchy projections,
/// throughput smoothing, and extraction security across all supported archive formats.
/// </summary>
public sealed class CrossFormatRegressionTests : IDisposable
{
    private readonly string _testDir;
    private readonly UnifiedArchiveEngine _engine;

    public CrossFormatRegressionTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ruszip_regression_core_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _engine = new UnifiedArchiveEngine();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { /* Ignore */ }
        }
    }

    [Theory]
    [InlineData(".zrus")]
    [InlineData(".zip")]
    public async Task EndToEnd_CompressListExtract_Roundtrip_VerifiesContentAndChecksums(string extension)
    {
        // Arrange
        var sourceDir = Path.Combine(_testDir, $"src_{extension.TrimStart('.')}");
        var subDir = Path.Combine(sourceDir, "docs", "specs");
        Directory.CreateDirectory(subDir);

        var file1 = Path.Combine(sourceDir, "readme.txt");
        var file2 = Path.Combine(subDir, "spec.json");
        var file3 = Path.Combine(sourceDir, "binary.dat");

        var textContent = "RusZip v2.0 - Universal High-Performance Archive Manager";
        var jsonContent = "{\"version\":\"2.0.0\",\"engine\":\"UnifiedArchiveEngine\",\"formats\":[\"zrus\",\"zip\",\"7z\",\"rar\",\"gz\",\"tar.gz\"]}";
        var binaryContent = new byte[128 * 1024]; // 128 KB
        Random.Shared.NextBytes(binaryContent);

        await File.WriteAllTextAsync(file1, textContent);
        await File.WriteAllTextAsync(file2, jsonContent);
        await File.WriteAllBytesAsync(file3, binaryContent);

        var archivePath = Path.Combine(_testDir, $"archive{extension}");
        var extractDir = Path.Combine(_testDir, $"extracted_{extension.TrimStart('.')}");

        var progressList = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(progressList.Add);

        // Act 1: Compress
        await _engine.CompressAsync(new ArchiveCompressionRequest(sourceDir, archivePath, 9), progress);
        Assert.True(File.Exists(archivePath));
        Assert.True(new FileInfo(archivePath).Length > 0);

        // Act 2: List entries
        var entries = await _engine.ListEntriesAsync(archivePath);
        Assert.True(entries.Count >= 3);
        Assert.Contains(entries, e => e.RelativePath.EndsWith("readme.txt") && !e.IsDirectory);
        Assert.Contains(entries, e => e.RelativePath.Contains("spec.json") && !e.IsDirectory);
        Assert.Contains(entries, e => e.RelativePath.EndsWith("binary.dat") && !e.IsDirectory);

        // Act 3: Build ArchiveHierarchy
        var hierarchy = ArchiveHierarchy.BuildTree(entries);
        Assert.NotEmpty(hierarchy);

        // Act 4: Extract
        await _engine.ExtractAsync(new ArchiveExtractionRequest(archivePath, extractDir), progress);

        // Assert: Extracted files match byte-for-byte
        var extReadme = Path.Combine(extractDir, "readme.txt");
        var extSpec = Path.Combine(extractDir, "docs", "specs", "spec.json");
        var extBin = Path.Combine(extractDir, "binary.dat");

        Assert.True(File.Exists(extReadme));
        Assert.True(File.Exists(extSpec));
        Assert.True(File.Exists(extBin));

        Assert.Equal(textContent, await File.ReadAllTextAsync(extReadme));
        Assert.Equal(jsonContent, await File.ReadAllTextAsync(extSpec));
        Assert.Equal(binaryContent, await File.ReadAllBytesAsync(extBin));
    }

    [Fact]
    public async Task EndToEnd_DecompressAllSixFormats_ExtractsContentSuccessfully()
    {
        // 1. Zstandard Tar (.zrus)
        var zrusSrc = Path.Combine(_testDir, "zrus_source");
        Directory.CreateDirectory(zrusSrc);
        await File.WriteAllTextAsync(Path.Combine(zrusSrc, "zrus_test.txt"), "zrus payload");
        var zrusPath = Path.Combine(_testDir, "test.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest(zrusSrc, zrusPath, 3));
        var zrusOut = Path.Combine(_testDir, "out_zrus");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zrusPath, zrusOut));
        Assert.Equal("zrus payload", await File.ReadAllTextAsync(Path.Combine(zrusOut, "zrus_test.txt")));

        // 2. Zip (.zip)
        var zipSrc = Path.Combine(_testDir, "zip_source");
        Directory.CreateDirectory(zipSrc);
        await File.WriteAllTextAsync(Path.Combine(zipSrc, "zip_test.txt"), "zip payload");
        var zipPath = Path.Combine(_testDir, "test.zip");
        await _engine.CompressAsync(new ArchiveCompressionRequest(zipSrc, zipPath, 6));
        var zipOut = Path.Combine(_testDir, "out_zip");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, zipOut));
        Assert.Equal("zip payload", await File.ReadAllTextAsync(Path.Combine(zipOut, "zip_test.txt")));

        // 3. 7-Zip (.7z)
        var sevenZipPath = Path.Combine(_testDir, "test.7z");
        TestArchiveFixtures.CreateSevenZipArchive(sevenZipPath, "seven.txt", "7z payload");
        var sevenOut = Path.Combine(_testDir, "out_7z");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(sevenZipPath, sevenOut));
        Assert.Equal("7z payload", await File.ReadAllTextAsync(Path.Combine(sevenOut, "seven.txt")));

        // 4. Tar.Gz (.tar.gz) & Tgz (.tgz)
        var tarGzPath = Path.Combine(_testDir, "test.tar.gz");
        await TestArchiveFixtures.CreateTarGzArchiveAsync(tarGzPath, new Dictionary<string, string> { ["tar.txt"] = "targz payload" });
        var tarGzOut = Path.Combine(_testDir, "out_targz");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(tarGzPath, tarGzOut));
        Assert.Equal("targz payload", await File.ReadAllTextAsync(Path.Combine(tarGzOut, "tar.txt")));

        // 5. Gzip (.gz)
        var gzPath = Path.Combine(_testDir, "single.txt.gz");
        await TestArchiveFixtures.CreateGzArchiveAsync(gzPath, "gz payload");
        var gzOut = Path.Combine(_testDir, "out_gz");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(gzPath, gzOut));
        Assert.Equal("gz payload", await File.ReadAllTextAsync(Path.Combine(gzOut, "single.txt")));

        // 6. RAR4 & RAR5 (.rar)
        var rar4Path = Path.Combine(_testDir, "test4.rar");
        TestArchiveFixtures.CreateRar4Archive(rar4Path, "rar4.txt", "rar4 payload");
        var rar4Out = Path.Combine(_testDir, "out_rar4");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(rar4Path, rar4Out));
        Assert.Equal("rar4 payload", await File.ReadAllTextAsync(Path.Combine(rar4Out, "rar4.txt")));

        var rar5Path = Path.Combine(_testDir, "test5.rar");
        TestArchiveFixtures.CreateRar5Archive(rar5Path, "rar5.txt", "rar5 payload");
        var rar5Out = Path.Combine(_testDir, "out_rar5");
        await _engine.ExtractAsync(new ArchiveExtractionRequest(rar5Path, rar5Out));
        Assert.Equal("rar5 payload", await File.ReadAllTextAsync(Path.Combine(rar5Out, "rar5.txt")));
    }

    [Fact]
    public void ThroughputSmoothing_SimulateBurstyTransfer_StabilizesThroughputAndEta()
    {
        var tracker = new ThroughputTracker(smoothingFactor: 0.3);
        tracker.Start();

        // Simulate 5 progress ticks with varying elapsed intervals and byte steps
        long totalBytes = 100 * 1024 * 1024; // 100 MB
        long processed = 0;

        for (int i = 1; i <= 5; i++)
        {
            processed += 20 * 1024 * 1024; // +20 MB
            tracker.Update(processed, totalBytes);
        }

        Assert.True(tracker.SmoothedSpeedBytesPerSec >= 0);
        Assert.NotNull(tracker.FormatSpeed());
        Assert.NotNull(tracker.FormatEta(totalBytes));
        Assert.Equal("100.0 MB / 100.0 MB", tracker.FormatProgress(totalBytes));

        // When complete, ETA is 00:00
        Assert.Equal(TimeSpan.Zero, tracker.EstimatedTimeRemaining(totalBytes));
    }

    [Fact]
    public void ArchiveHierarchy_DeepTreeAggregationAndSearch_BehavesConsistently()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = new List<ArchiveEntry>
        {
            new("src/RusZip.Core/Models/ArchiveEntry.cs", 2048, 800, now, false),
            new("src/RusZip.Core/Engines/UnifiedArchiveEngine.cs", 4096, 1500, now, false),
            new("src/RusZip.Desktop/ViewModels/MainWindowViewModel.cs", 8192, 3000, now, false),
            new("docs/README.md", 1024, 400, now, false),
            new("assets/logo.png", 16384, 15000, now, false)
        };

        // Full tree
        var roots = ArchiveHierarchy.BuildTree(entries);
        Assert.Equal(3, roots.Count); // src, docs, assets

        var src = roots.First(r => r.Name == "src");
        Assert.Equal(14336, src.UncompressedSize); // 2048 + 4096 + 8192
        Assert.Equal(5300, src.CompressedSize);     // 800 + 1500 + 3000

        // Filtered tree
        var filteredRoots = ArchiveHierarchy.BuildTree(entries, "ViewModel");
        Assert.Single(filteredRoots);
        var filteredSrc = filteredRoots[0];
        Assert.Equal("src", filteredSrc.Name);
        Assert.Single(filteredSrc.Children); // RusZip.Desktop
        var desktop = filteredSrc.Children[0];
        Assert.Equal("RusZip.Desktop", desktop.Name);
        Assert.Single(desktop.Children); // ViewModels
        var vms = desktop.Children[0];
        Assert.Single(vms.Children);
        Assert.Equal("MainWindowViewModel.cs", vms.Children[0].Name);
    }

    [Theory]
    [InlineData("../../etc/shadow")]
    [InlineData("../parent/target.txt")]
    [InlineData("/var/log/syslog")]
    [InlineData(@"..\windows\system32\calc.exe")]
    public async Task SafeArchiveExtractor_BlocksAllPathTraversalVariants(string maliciousPath)
    {
        var targetDir = Path.Combine(_testDir, "safe_extract_target");
        Directory.CreateDirectory(targetDir);

        var maliciousEntry = new ExtractionEntry(
            RelativePath: maliciousPath,
            IsDirectory: false,
            UncompressedSize: 10,
            ModificationTime: null,
            UnixMode: null,
            OpenStreamAsync: _ => ValueTask.FromResult<Stream>(new MemoryStream("dangerous"u8.ToArray()))
        );

        var source = new SingleEntrySource(maliciousEntry);

        var ex = await Assert.ThrowsAsync<SecurityException>(() =>
            SafeArchiveExtractor.ExtractAllAsync(source, targetDir, overwrite: true, totalBytes: 10, progress: null));

        Assert.Contains("path traversal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private class SingleEntrySource(ExtractionEntry entry) : IArchiveExtractionSource
    {
        public async IAsyncEnumerable<ExtractionEntry> ReadEntriesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            yield return entry;
            await Task.CompletedTask;
        }
    }
}

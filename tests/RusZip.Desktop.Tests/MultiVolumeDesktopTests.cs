using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using RusZip.Desktop.ViewModels;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class MultiVolumeDesktopTests : IDisposable
{
    private readonly string _tempDir;

    public MultiVolumeDesktopTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ruszip_desktop_mv_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { /* Ignore */ }
        }
    }

    private class MultiVolumeMockArchiveEngine : IArchiveEngine
    {
        public Task CompressAsync(ArchiveCompressionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<AppendResult> AppendAsync(ArchiveAppendRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new AppendResult(true, request.ArchivePath, "zrus", 0, 0, 0, 0, 0, 0, 0, 1.0, 0));
        public Task<ArchiveDeleteResult> DeleteEntriesAsync(ArchiveDeleteRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new ArchiveDeleteResult(true, request.ArchivePath, 0, 0, 0, 0, 0));
        public Task<ExtractionResult> ExtractAsync(ArchiveExtractionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new ExtractionResult(100, 1, 1));
        public Task<ArchiveTestResult> TestArchiveAsync(string archivePath, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new ArchiveTestResult(true, archivePath, "zrus", 1, 100, 10.0, TimeSpan.Zero, []));
        public Task<ArchiveTestResult> TestArchiveAsync(string archivePath, string? password, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new ArchiveTestResult(true, archivePath, "zrus", 1, 100, 10.0, TimeSpan.Zero, []));
        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ArchiveEntry>>([new ArchiveEntry("file.txt", 100, 50, DateTimeOffset.UtcNow, false)]);
        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, string? password, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ArchiveEntry>>([new ArchiveEntry("file.txt", 100, 50, DateTimeOffset.UtcNow, false)]);
        public Task<bool> IsEncryptedAsync(string archivePath, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<IReadOnlyList<string>> GetVolumePartsAsync(string archivePath, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([archivePath]);
    }

    [Fact]
    public void CompressionSettingsViewModel_SplitVolumeValidation_WorksCorrectly()
    {
        var vm = new CompressionSettingsViewModel();
        vm.StageSources([CreateDummyFile("source.txt")]);
        vm.DestinationPath = Path.Combine(_tempDir, "archive.zrus");

        Assert.False(vm.IsSplitVolume);
        Assert.True(vm.CanCompress);
        Assert.Null(vm.SplitSizeErrorMessage);

        // Turn on split volume with default preset "100 MB"
        vm.IsSplitVolume = true;
        vm.SelectedSplitPreset = "100 MB";
        Assert.True(vm.CanCompress);
        Assert.Null(vm.SplitSizeErrorMessage);
        Assert.Equal(100L * 1024 * 1024, vm.ResolvedSplitSizeBytes);

        // Custom preset with invalid size
        vm.SelectedSplitPreset = "Custom...";
        vm.CustomSplitSize = "invalid";
        Assert.False(vm.CanCompress);
        Assert.NotNull(vm.SplitSizeErrorMessage);

        // Custom preset under 64 KB
        vm.CustomSplitSize = "10KB";
        Assert.False(vm.CanCompress);
        Assert.Contains("64 KB", vm.SplitSizeErrorMessage);

        // Custom preset valid
        vm.CustomSplitSize = "500MB";
        Assert.True(vm.CanCompress);
        Assert.Null(vm.SplitSizeErrorMessage);
        Assert.Equal(500L * 1024 * 1024, vm.ResolvedSplitSizeBytes);

        // Request creation includes split size
        var req = vm.CreateCompressionRequest();
        Assert.Equal(500L * 1024 * 1024, req.SplitSizeBytes);
    }

    [Fact]
    public async Task MainWindowViewModel_MultiVolumeArchive_ShowsMultiVolumeBadge()
    {
        // Create dummy part1, part2, part3 files
        var p1 = CreateDummyFile("backup.part1.zrus");
        var p2 = CreateDummyFile("backup.part2.zrus");
        var p3 = CreateDummyFile("backup.part3.zrus");

        var mockEngine = new MultiVolumeMockArchiveEngine();
        var vm = new MainWindowViewModel(mockEngine);

        await vm.OpenArchiveAsync(p1);

        Assert.True(vm.HasOpenArchive);
        Assert.Contains("📦 Multi-Volume (3 parts)", vm.FormatCapabilityBadge);
    }

    private string CreateDummyFile(string filename)
    {
        var path = Path.Combine(_tempDir, filename);
        File.WriteAllText(path, "Sample Data");
        return path;
    }
}

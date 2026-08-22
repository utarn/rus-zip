using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Tests;

public class MainWindowViewModelTests
{
    private class FakeArchiveEngine : IArchiveEngine
    {
        public List<ArchiveEntry> EntriesToReturn { get; set; } = [];
        public Exception? ExceptionToThrow { get; set; }
        public ArchiveCompressionRequest? LastCompressionRequest { get; private set; }
        public ArchiveExtractionRequest? LastExtractionRequest { get; private set; }

        public Task CompressAsync(ArchiveCompressionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            if (ExceptionToThrow != null) throw ExceptionToThrow;
            LastCompressionRequest = request;
            progress?.Report(new ProgressReport(100, 100, "compressing", 100.0, 1, 1));
            return Task.CompletedTask;
        }

        public Task ExtractAsync(ArchiveExtractionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            if (ExceptionToThrow != null) throw ExceptionToThrow;
            LastExtractionRequest = request;
            progress?.Report(new ProgressReport(100, 100, "extracting", 100.0, 1, 1));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, CancellationToken ct = default)
        {
            if (ExceptionToThrow != null) throw ExceptionToThrow;
            return Task.FromResult<IReadOnlyList<ArchiveEntry>>(EntriesToReturn);
        }
    }

    [Theory]
    [InlineData("test.zrus", true)]
    [InlineData("test.zip", true)]
    [InlineData("test.rar", true)]
    [InlineData("test.7z", true)]
    [InlineData("test.gz", true)]
    [InlineData("test.tar.gz", true)]
    [InlineData("test.tgz", true)]
    [InlineData("test.tar", true)]
    [InlineData("TEST.ZIP", true)]
    [InlineData("TEST.TAR.GZ", true)]
    [InlineData("test.txt", false)]
    [InlineData("test.exe", false)]
    [InlineData("test.pdf", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupportedArchive_IdentifiesExtensionsCorrectly(string? path, bool expected)
    {
        var result = MainWindowViewModel.IsSupportedArchive(path);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task OpenArchiveAsync_LoadsEntries_AndUpdatesState()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn =
                [
                    new ArchiveEntry("file1.txt", 100, 50, DateTimeOffset.UtcNow, false),
                    new ArchiveEntry("folder/file2.txt", 200, 100, DateTimeOffset.UtcNow, false)
                ]
            };

            var vm = new MainWindowViewModel(fakeEngine);
            Assert.False(vm.HasOpenArchive);

            await vm.OpenArchiveAsync(tempFile);

            Assert.True(vm.HasOpenArchive);
            Assert.Equal(2, vm.Browser.TotalEntries);
            Assert.Equal(tempFile, vm.Browser.LoadedArchivePath);
            Assert.Contains("Loaded 2 entries", vm.StatusText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task OpenArchiveAsync_NonExistentFile_DoesNotOpen()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);

        await vm.OpenArchiveAsync("/non/existent/path/archive.zrus");

        Assert.False(vm.HasOpenArchive);
        Assert.Equal("Ready", vm.StatusText);
    }

    [Fact]
    public async Task OpenArchiveAsync_EngineThrows_SetsStatusTextWithErrorMessage()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                ExceptionToThrow = new InvalidDataException("Archive header is corrupted")
            };

            var vm = new MainWindowViewModel(fakeEngine);
            await vm.OpenArchiveAsync(tempFile);

            Assert.False(vm.HasOpenArchive);
            Assert.Contains("Failed to open archive: Archive header is corrupted", vm.StatusText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task HandleDroppedPathsAsync_SupportedArchive_OpensDirectly()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zrus");
        await File.WriteAllTextAsync(tempFile, "fake archive content");
        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn = [new ArchiveEntry("item.txt", 10, 5, null, false)]
            };
            var vm = new MainWindowViewModel(fakeEngine);

            await vm.HandleDroppedPathsAsync([tempFile]);

            Assert.True(vm.HasOpenArchive);
            Assert.Equal(tempFile, vm.Browser.LoadedArchivePath);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task HandleDroppedPathsAsync_NonArchiveFile_OpensCompressDialog()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(tempFile, "plain text");
        try
        {
            var fakeEngine = new FakeArchiveEngine();
            var vm = new MainWindowViewModel(fakeEngine);

            await vm.HandleDroppedPathsAsync([tempFile]);

            Assert.False(vm.HasOpenArchive);
            Assert.True(vm.IsCompressDialogVisible);
            Assert.Equal(tempFile, vm.Settings.SourcePath);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExecuteCompressAsync_CallsEngineCompress_AndOpensArchive()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempDest = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zrus");
        Directory.CreateDirectory(tempDir);
        await File.WriteAllTextAsync(tempDest, "created archive");

        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn = [new ArchiveEntry("file.txt", 100, 50, null, false)]
            };
            var vm = new MainWindowViewModel(fakeEngine);

            vm.Settings.SourcePath = tempDir;
            vm.Settings.DestinationPath = tempDest;
            vm.IsCompressDialogVisible = true;

            await vm.ExecuteCompressAsync();

            Assert.NotNull(fakeEngine.LastCompressionRequest);
            Assert.Equal(tempDir, fakeEngine.LastCompressionRequest.SourcePath);
            Assert.Equal(tempDest, fakeEngine.LastCompressionRequest.DestinationArchivePath);
            Assert.False(vm.IsCompressDialogVisible);
            Assert.True(vm.HasOpenArchive);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (File.Exists(tempDest)) File.Delete(tempDest);
        }
    }

    [Fact]
    public async Task ExecuteExtractAllAsync_CallsEngineExtract_AndUpdatesStatus()
    {
        var tempArchive = Path.GetTempFileName();
        var destDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn = [new ArchiveEntry("file.txt", 100, 50, null, false)]
            };
            var vm = new MainWindowViewModel(fakeEngine);

            await vm.OpenArchiveAsync(tempArchive);
            Assert.True(vm.HasOpenArchive);

            await vm.ExecuteExtractAllAsync(destDir);

            Assert.NotNull(fakeEngine.LastExtractionRequest);
            Assert.Equal(tempArchive, fakeEngine.LastExtractionRequest.ArchivePath);
            Assert.Equal(destDir, fakeEngine.LastExtractionRequest.DestinationDirectory);
            Assert.True(fakeEngine.LastExtractionRequest.Overwrite);
            Assert.Contains($"Extracted to {destDir}", vm.StatusText);
        }
        finally
        {
            if (File.Exists(tempArchive)) File.Delete(tempArchive);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        }
    }

    [Fact]
    public async Task BrowserExtractRequested_DelegatesToRequestExtractDestinationFolder()
    {
        var tempArchive = Path.GetTempFileName();
        var destDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn = [new ArchiveEntry("file.txt", 100, 50, null, false)]
            };
            var vm = new MainWindowViewModel(fakeEngine);
            vm.RequestExtractDestinationFolder = () => Task.FromResult<string?>(destDir);

            await vm.OpenArchiveAsync(tempArchive);

            await vm.Browser.RequestExtractAsync();

            Assert.NotNull(fakeEngine.LastExtractionRequest);
            Assert.Equal(destDir, fakeEngine.LastExtractionRequest.DestinationDirectory);
        }
        finally
        {
            if (File.Exists(tempArchive)) File.Delete(tempArchive);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        }
    }

    [Fact]
    public void CloseArchive_ResetsState()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine)
        {
            HasOpenArchive = true,
            StatusText = "Active archive"
        };

        vm.CloseArchive();

        Assert.False(vm.HasOpenArchive);
        Assert.Equal(string.Empty, vm.Browser.LoadedArchivePath);
        Assert.Equal("Ready", vm.StatusText);
    }

    [Fact]
    public void CompressDialog_ShowAndClose_ControlsVisibility()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);

        Assert.False(vm.IsCompressDialogVisible);

        vm.ShowCompressDialog("/path/to/folder");
        Assert.True(vm.IsCompressDialogVisible);
        Assert.Equal("/path/to/folder", vm.Settings.SourcePath);
        Assert.Equal("/path/to/folder.zrus", vm.Settings.DestinationPath);

        vm.CloseCompressDialog();
        Assert.False(vm.IsCompressDialogVisible);
    }
}

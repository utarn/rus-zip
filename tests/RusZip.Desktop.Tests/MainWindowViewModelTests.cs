using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using RusZip.Desktop.Models;
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
        public Action? OnCompress { get; set; }

        public Task CompressAsync(ArchiveCompressionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            if (ExceptionToThrow != null) throw ExceptionToThrow;
            if (!string.IsNullOrEmpty(request.DestinationArchivePath))
            {
                var dir = Path.GetDirectoryName(request.DestinationArchivePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (!File.Exists(request.DestinationArchivePath))
                {
                    File.WriteAllBytes(request.DestinationArchivePath, [0x01]);
                }
            }
            OnCompress?.Invoke();
            LastCompressionRequest = request;
            progress?.Report(new ProgressReport(100, 100, "compressing", 100.0, 1, 1));
            return Task.CompletedTask;
        }

        public Task<ExtractionResult> ExtractAsync(ArchiveExtractionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            if (ExceptionToThrow != null) throw ExceptionToThrow;
            LastExtractionRequest = request;
            progress?.Report(new ProgressReport(100, 100, "extracting", 100.0, 1, 1));
            return Task.FromResult(new ExtractionResult(100, 1, 1));
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
    [InlineData("test.tar", false)]
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
            Assert.Equal(tempFile + ".zrus", vm.Settings.DestinationPath);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task HandleDroppedPathsAsync_Directory_OpensCompressDialog()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var fakeEngine = new FakeArchiveEngine();
            var vm = new MainWindowViewModel(fakeEngine);

            await vm.HandleDroppedPathsAsync([tempDir]);

            Assert.False(vm.HasOpenArchive);
            Assert.True(vm.IsCompressDialogVisible);
            Assert.Equal(tempDir, vm.Settings.SourcePath);
            Assert.Equal(tempDir + ".zrus", vm.Settings.DestinationPath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task HandleDroppedPathsAsync_EmptyList_DoesNothing()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);

        await vm.HandleDroppedPathsAsync([]);

        Assert.False(vm.HasOpenArchive);
        Assert.False(vm.IsCompressDialogVisible);
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
            vm.Settings.CompressionLevel = 15;
            vm.IsCompressDialogVisible = true;

            await vm.ExecuteCompressAsync();

            Assert.NotNull(fakeEngine.LastCompressionRequest);
            Assert.Equal(tempDir, fakeEngine.LastCompressionRequest.SourcePath);
            Assert.Equal(tempDest, fakeEngine.LastCompressionRequest.DestinationArchivePath);
            Assert.Equal(15, fakeEngine.LastCompressionRequest.CompressionLevel);
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
    public async Task ExecuteCompressAsync_EmptySourceOrDest_DoesNotExecute()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);

        vm.Settings.SourcePath = "";
        vm.Settings.DestinationPath = "";

        await vm.ExecuteCompressAsync();

        Assert.Null(fakeEngine.LastCompressionRequest);
    }

    [Fact]
    public async Task ExecuteCompressAsync_WhenEngineThrows_SetsStatusText()
    {
        var fakeEngine = new FakeArchiveEngine
        {
            ExceptionToThrow = new IOException("Disk full")
        };
        var vm = new MainWindowViewModel(fakeEngine);

        vm.Settings.SourcePath = "/path/source";
        vm.Settings.DestinationPath = "/path/dest.zrus";

        await vm.ExecuteCompressAsync();

        Assert.Contains("Compression failed: Disk full", vm.StatusText);
    }

    [Fact]
    public async Task ExecuteCompressAsync_WhenCancelled_SetsStatusTextAndCleansUpFile()
    {
        var tempDest = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zrus");
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);

        fakeEngine.OnCompress = () =>
        {
            File.WriteAllText(tempDest, "partially written file");
            vm.Progress.CancelCommand.Execute(null);
            throw new OperationCanceledException();
        };

        try
        {
            vm.Settings.SourcePath = "/path/source";
            vm.Settings.DestinationPath = tempDest;

            await vm.ExecuteCompressAsync();

            Assert.Equal("Compression cancelled.", vm.StatusText);
            Assert.False(File.Exists(tempDest));
        }
        finally
        {
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
    public async Task ExecuteExtractAllAsync_PassesExtractionLimitsFromSettings()
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
            vm.Browser.ExtractionSettings.MaxUncompressedSizeText = "2GB";
            vm.Browser.ExtractionSettings.MaxEntryCount = 500;

            await vm.OpenArchiveAsync(tempArchive);
            await vm.ExecuteExtractAllAsync(destDir);

            Assert.NotNull(fakeEngine.LastExtractionRequest);
            Assert.NotNull(fakeEngine.LastExtractionRequest.Limits);
            Assert.Equal(2L * 1024 * 1024 * 1024, fakeEngine.LastExtractionRequest.Limits.MaxCumulativeUncompressedBytes);
            Assert.Equal(500, fakeEngine.LastExtractionRequest.Limits.MaxEntryCount);
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

    [Fact]
    public void ToggleTheme_CyclesThroughAllThemes()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);

        Assert.Equal(ThemeMode.System, vm.CurrentTheme);
        Assert.Equal("System", vm.ThemeDisplayName);

        vm.ToggleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.Dark, vm.CurrentTheme);
        Assert.Equal("Dark", vm.ThemeDisplayName);

        vm.ToggleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.Light, vm.CurrentTheme);
        Assert.Equal("Light", vm.ThemeDisplayName);

        vm.ToggleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.System, vm.CurrentTheme);
        Assert.Equal("System", vm.ThemeDisplayName);
    }

    [Fact]
    public void SettingCurrentTheme_NotifiesAllThemeRelatedProperties()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);
        var changedProperties = new HashSet<string>();

        vm.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
            {
                changedProperties.Add(e.PropertyName);
            }
        };

        vm.CurrentTheme = ThemeMode.Light;

        Assert.Contains(nameof(MainWindowViewModel.CurrentTheme), changedProperties);
        Assert.Contains(nameof(MainWindowViewModel.IsDarkTheme), changedProperties);
        Assert.Contains(nameof(MainWindowViewModel.IsLightTheme), changedProperties);
        Assert.Contains(nameof(MainWindowViewModel.IsSystemTheme), changedProperties);
        Assert.Contains(nameof(MainWindowViewModel.ThemeIconKey), changedProperties);
        Assert.Contains(nameof(MainWindowViewModel.ThemeDisplayName), changedProperties);
    }

    [Fact]
    public async Task ExecuteExtractItemAsync_ExtractsArchiveAndUpdatesStatus()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);
        var tempFile = Path.GetTempFileName();
        var destDir = Path.Combine(Path.GetTempPath(), $"dest_{Guid.NewGuid()}");

        try
        {
            fakeEngine.EntriesToReturn = [new ArchiveEntry("file1.txt", 100, 50, null, false)];
            await vm.OpenArchiveAsync(tempFile);

            var item = vm.Browser.FindItemByPath("file1.txt");
            Assert.NotNull(item);

            await vm.ExecuteExtractItemAsync(item, destDir);

            Assert.NotNull(fakeEngine.LastExtractionRequest);
            Assert.Equal(tempFile, fakeEngine.LastExtractionRequest.ArchivePath);
            Assert.Equal(destDir, fakeEngine.LastExtractionRequest.DestinationDirectory);
            Assert.Equal($"Extracted file1.txt to {destDir}", vm.StatusText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        }
    }

    [Fact]
    public async Task ExtractSelectedItemCommand_InvokesItemExtractionWorkflow()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);
        var tempFile = Path.GetTempFileName();
        var destDir = Path.Combine(Path.GetTempPath(), $"dest_{Guid.NewGuid()}");

        try
        {
            fakeEngine.EntriesToReturn = [new ArchiveEntry("file1.txt", 100, 50, null, false)];
            await vm.OpenArchiveAsync(tempFile);

            vm.RequestExtractDestinationFolder = () => Task.FromResult<string?>(destDir);

            var item = vm.Browser.FindItemByPath("file1.txt");
            vm.Browser.SelectedItem = item;

            await vm.ExtractSelectedItemCommand.ExecuteAsync(null);

            Assert.NotNull(fakeEngine.LastExtractionRequest);
            Assert.Equal(destDir, fakeEngine.LastExtractionRequest.DestinationDirectory);
            Assert.Equal($"Extracted file1.txt to {destDir}", vm.StatusText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        }
    }

    [Fact]
    public async Task CopyPathCommand_OnMainWindowViewModel_DelegatesToBrowser()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);
        var tempFile = Path.GetTempFileName();

        try
        {
            fakeEngine.EntriesToReturn = [new ArchiveEntry("docs/readme.txt", 100, 50, null, false)];
            await vm.OpenArchiveAsync(tempFile);

            string? copied = null;
            vm.Browser.CopyToClipboardService = text =>
            {
                copied = text;
                return Task.CompletedTask;
            };

            var item = vm.Browser.FindItemByPath("docs/readme.txt");
            vm.Browser.SelectedItem = item;

            await vm.CopyPathCommand.ExecuteAsync(null);
            Assert.Equal("docs/readme.txt", copied);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void IsDragOver_InitializesFalse_AndCanBeToggled()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);

        Assert.False(vm.IsDragOver);

        vm.IsDragOver = true;
        Assert.True(vm.IsDragOver);

        vm.IsDragOver = false;
        Assert.False(vm.IsDragOver);
    }

    [Fact]
    public void CreateArchiveCommand_OpensCompressDialog()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);

        Assert.False(vm.IsCompressDialogVisible);

        vm.CreateArchiveCommand.Execute(null);

        Assert.True(vm.IsCompressDialogVisible);
    }

    [Fact]
    public async Task OpenArchivePickerCommand_LoadsArchiveWhenPathSelected()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn = [new ArchiveEntry("file1.txt", 100, 50, null, false)]
            };
            var vm = new MainWindowViewModel(fakeEngine)
            {
                RequestOpenArchivePicker = () => Task.FromResult<string?>(tempFile)
            };

            await vm.OpenArchivePickerCommand.ExecuteAsync(null);

            Assert.True(vm.HasOpenArchive);
            Assert.Equal(tempFile, vm.Browser.LoadedArchivePath);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task OpenArchivePickerCommand_DoesNothingWhenCancelled()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine)
        {
            RequestOpenArchivePicker = () => Task.FromResult<string?>(null)
        };

        await vm.OpenArchivePickerCommand.ExecuteAsync(null);

        Assert.False(vm.HasOpenArchive);
    }
}

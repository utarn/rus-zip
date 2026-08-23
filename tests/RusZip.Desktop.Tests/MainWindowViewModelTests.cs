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

        public ArchiveAppendRequest? LastAppendRequest { get; private set; }
        public ArchiveDeleteRequest? LastDeleteRequest { get; private set; }
        public Action? OnAppend { get; set; }
        public Action? OnDelete { get; set; }
        public AppendResult? AppendResultToReturn { get; set; }
        public ArchiveDeleteResult? DeleteResultToReturn { get; set; }

        public Task<AppendResult> AppendAsync(ArchiveAppendRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            if (ExceptionToThrow != null) throw ExceptionToThrow;
            LastAppendRequest = request;
            OnAppend?.Invoke();
            progress?.Report(new ProgressReport(100, 100, "appending", 100.0, 1, 1));
            return Task.FromResult(AppendResultToReturn ?? new AppendResult(true, request.ArchivePath, "zrus", request.SourcePaths.Count, 0, 0, 0, request.SourcePaths.Count, 100, 50, 2.0, 10));
        }

        public Task<ArchiveDeleteResult> DeleteEntriesAsync(ArchiveDeleteRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            if (ExceptionToThrow != null) throw ExceptionToThrow;
            LastDeleteRequest = request;
            OnDelete?.Invoke();
            progress?.Report(new ProgressReport(100, 100, "deleting", 100.0, 1, 1));
            return Task.FromResult(DeleteResultToReturn ?? new ArchiveDeleteResult(true, request.ArchivePath, request.EntryPaths.Count, 0, 100, 50, 10));
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
    [InlineData("test.tar.zstd", true)]
    [InlineData("test.tzstd", true)]
    [InlineData("test.zst", true)]
    [InlineData("test.zip", true)]
    [InlineData("test.rar", true)]
    [InlineData("test.7z", true)]
    [InlineData("test.gz", true)]
    [InlineData("test.tar.gz", true)]
    [InlineData("test.tgz", true)]
    [InlineData("test.tar", false)]
    [InlineData("TEST.ZIP", true)]
    [InlineData("TEST.TAR.GZ", true)]
    [InlineData("TEST.TAR.ZSTD", true)]
    [InlineData("TEST.TZSTD", true)]
    [InlineData("TEST.ZST", true)]
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
    public async Task StatusText_StripsControlBytesAndCollapsesToSingleLine()
    {
        char esc = (char)0x1b;
        var tempFile = Path.GetTempFileName();
        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                ExceptionToThrow = new InvalidDataException($"boom{esc}[31m{esc}[0m\nsecond line")
            };

            var vm = new MainWindowViewModel(fakeEngine);
            await vm.OpenArchiveAsync(tempFile);

            Assert.Contains("Failed to open archive: boom[31m[0m second line", vm.StatusText);
            Assert.DoesNotContain(esc, vm.StatusText);
            Assert.DoesNotContain('\n', vm.StatusText);
            Assert.DoesNotContain('\r', vm.StatusText);
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
    public async Task HandleDroppedPathsAsync_MultipleNonArchiveFiles_StagesAllPaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var file1 = Path.Combine(dir, "a.txt");
        var file2 = Path.Combine(dir, "b.txt");
        var file3 = Path.Combine(dir, "c.txt");
        await File.WriteAllTextAsync(file1, "one");
        await File.WriteAllTextAsync(file2, "two");
        await File.WriteAllTextAsync(file3, "three");

        try
        {
            var fakeEngine = new FakeArchiveEngine();
            var vm = new MainWindowViewModel(fakeEngine);

            await vm.HandleDroppedPathsAsync([file1, file2, file3]);

            Assert.False(vm.HasOpenArchive);
            Assert.True(vm.IsCompressDialogVisible);
            Assert.Equal(3, vm.Settings.SourcePaths.Count);
            Assert.Equal(file1, vm.Settings.SourcePaths[0]);
            Assert.Equal(file2, vm.Settings.SourcePaths[1]);
            Assert.Equal(file3, vm.Settings.SourcePaths[2]);
            Assert.True(vm.Settings.HasMultipleSources);
            Assert.Contains(file1, vm.Settings.SourcePathsDisplay);
            Assert.Contains(file2, vm.Settings.SourcePathsDisplay);
            Assert.Equal(file1, vm.Settings.SourcePath);
            Assert.Equal(Path.Combine(dir, "Archive.zrus"), vm.Settings.DestinationPath);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task HandleDroppedPathsAsync_MultipleArchives_OpensFirstAndReportsRestIgnored()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var archive1 = Path.Combine(dir, "first.zrus");
        var archive2 = Path.Combine(dir, "second.zrus");
        var archive3 = Path.Combine(dir, "third.zrus");
        await File.WriteAllTextAsync(archive1, "a1");
        await File.WriteAllTextAsync(archive2, "a2");
        await File.WriteAllTextAsync(archive3, "a3");

        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn = [new ArchiveEntry("item.txt", 10, 5, null, false)]
            };
            var vm = new MainWindowViewModel(fakeEngine);

            await vm.HandleDroppedPathsAsync([archive1, archive2, archive3]);

            Assert.True(vm.HasOpenArchive);
            Assert.Equal(archive1, vm.Browser.LoadedArchivePath);
            Assert.False(vm.IsCompressDialogVisible);
            Assert.Contains("2 other archives ignored", vm.StatusText);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task HandleDroppedPathsAsync_MixedArchivesAndNonArchives_StagesNonArchivesAndReportsIgnored()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var archive1 = Path.Combine(dir, "existing.zrus");
        var file1 = Path.Combine(dir, "a.txt");
        var file2 = Path.Combine(dir, "b.txt");
        await File.WriteAllTextAsync(archive1, "archive");
        await File.WriteAllTextAsync(file1, "one");
        await File.WriteAllTextAsync(file2, "two");

        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn = [new ArchiveEntry("item.txt", 10, 5, null, false)]
            };
            var vm = new MainWindowViewModel(fakeEngine);

            await vm.HandleDroppedPathsAsync([archive1, file1, file2]);

            // Non-archive items win: they are staged for the wizard; the archive is reported ignored.
            Assert.False(vm.HasOpenArchive);
            Assert.True(vm.IsCompressDialogVisible);
            Assert.Equal(2, vm.Settings.SourcePaths.Count);
            Assert.Equal(file1, vm.Settings.SourcePaths[0]);
            Assert.Equal(file2, vm.Settings.SourcePaths[1]);
            Assert.Contains("1 archive ignored", vm.StatusText);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
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
    public async Task ExecuteExtractItemAsync_PassesFileRelativePathAsEntryFilter()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);
        var tempFile = Path.GetTempFileName();
        var destDir = Path.Combine(Path.GetTempPath(), $"dest_{Guid.NewGuid()}");

        try
        {
            fakeEngine.EntriesToReturn =
            [
                new ArchiveEntry("folder/file1.txt", 100, 50, null, false),
                new ArchiveEntry("folder/file2.txt", 200, 100, null, false)
            ];
            await vm.OpenArchiveAsync(tempFile);

            var fileItem = vm.Browser.FindItemByPath("folder/file1.txt");
            Assert.NotNull(fileItem);
            Assert.False(fileItem.IsDirectory);

            await vm.ExecuteExtractItemAsync(fileItem, destDir);

            Assert.NotNull(fakeEngine.LastExtractionRequest);
            Assert.NotNull(fakeEngine.LastExtractionRequest.Entries);
            Assert.Equal(new[] { "folder/file1.txt" }, fakeEngine.LastExtractionRequest.Entries);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        }
    }

    [Fact]
    public async Task ExecuteExtractItemAsync_PassesFolderRelativePathAsEntryFilter()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);
        var tempFile = Path.GetTempFileName();
        var destDir = Path.Combine(Path.GetTempPath(), $"dest_{Guid.NewGuid()}");

        try
        {
            fakeEngine.EntriesToReturn =
            [
                new ArchiveEntry("folder/file1.txt", 100, 50, null, false),
                new ArchiveEntry("folder/sub/file2.txt", 200, 100, null, false)
            ];
            await vm.OpenArchiveAsync(tempFile);

            var folderItem = vm.Browser.FindItemByPath("folder");
            Assert.NotNull(folderItem);
            Assert.True(folderItem.IsDirectory);

            await vm.ExecuteExtractItemAsync(folderItem, destDir);

            Assert.NotNull(fakeEngine.LastExtractionRequest);
            Assert.NotNull(fakeEngine.LastExtractionRequest.Entries);
            Assert.Equal(new[] { "folder" }, fakeEngine.LastExtractionRequest.Entries);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        }
    }

    [Fact]
    public async Task ExecuteExtractItemAsync_KeepsLimitsAndOverwriteDefaults()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);
        var tempFile = Path.GetTempFileName();
        var destDir = Path.Combine(Path.GetTempPath(), $"dest_{Guid.NewGuid()}");

        try
        {
            fakeEngine.EntriesToReturn =
            [
                new ArchiveEntry("docs/readme.txt", 100, 50, null, false),
                new ArchiveEntry("docs/guide.txt", 200, 100, null, false)
            ];
            await vm.OpenArchiveAsync(tempFile);
            vm.Browser.ExtractionSettings.MaxUncompressedSizeText = "2GB";
            vm.Browser.ExtractionSettings.MaxEntryCount = 500;

            var fileItem = vm.Browser.FindItemByPath("docs/readme.txt");
            Assert.NotNull(fileItem);
            await vm.ExecuteExtractItemAsync(fileItem, destDir);

            Assert.NotNull(fakeEngine.LastExtractionRequest);
            Assert.True(fakeEngine.LastExtractionRequest.Overwrite);
            Assert.NotNull(fakeEngine.LastExtractionRequest.Limits);
            Assert.Equal(2L * 1024 * 1024 * 1024, fakeEngine.LastExtractionRequest.Limits.MaxCumulativeUncompressedBytes);
            Assert.Equal(500, fakeEngine.LastExtractionRequest.Limits.MaxEntryCount);
            Assert.Equal(new[] { "docs/readme.txt" }, fakeEngine.LastExtractionRequest.Entries);
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

    [Fact]
    public async Task OpenSettingsCommand_OpensDialog_AndLoadsAssociations()
    {
        var fakeEngine = new FakeArchiveEngine();
        var vm = new MainWindowViewModel(fakeEngine);

        Assert.False(vm.IsSettingsDialogVisible);

        await vm.OpenSettingsCommand.ExecuteAsync(null);

        Assert.True(vm.IsSettingsDialogVisible);
        Assert.NotEmpty(vm.SettingsViewModel.Formats);

        vm.CloseSettingsDialogCommand.Execute(null);

        Assert.False(vm.IsSettingsDialogVisible);
    }

    [Theory]
    [InlineData("archive.tar.zstd")]
    [InlineData("archive.tzstd")]
    [InlineData("archive.zst")]
    public async Task HandleDroppedPathsAsync_ZstandardFormats_OpensArchiveDirectly(string fileName)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{fileName}");
        await File.WriteAllTextAsync(tempFile, "fake archive content");
        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn = [new ArchiveEntry("file.txt", 100, 50, null, false)]
            };
            var vm = new MainWindowViewModel(fakeEngine);

            await vm.HandleDroppedPathsAsync([tempFile]);

            Assert.True(vm.HasOpenArchive);
            Assert.Equal(tempFile, vm.Browser.LoadedArchivePath);
            Assert.False(vm.IsCompressDialogVisible);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void MainWindow_OpenArchivePickerFileTypes_IncludesZstandardFormatsAndDedicatedCategories()
    {
        var filters = Views.MainWindow.OpenArchivePickerFileTypes;
        Assert.NotNull(filters);

        // 1. Supported Archives contains all formats including .tar.zstd, .tzstd, and .zst
        var supported = filters.FirstOrDefault(f => f.Name.StartsWith("Supported Archives"));
        Assert.NotNull(supported);
        Assert.NotNull(supported.Patterns);
        Assert.Contains("*.tar.zstd", supported.Patterns);
        Assert.Contains("*.tzstd", supported.Patterns);
        Assert.Contains("*.zst", supported.Patterns);
        Assert.Contains("*.zrus", supported.Patterns);
        Assert.Contains("*.zip", supported.Patterns);

        // 2. Dedicated Zstandard Tar Archives category
        var zstdTar = filters.FirstOrDefault(f => f.Name.StartsWith("Zstandard Tar Archives"));
        Assert.NotNull(zstdTar);
        Assert.NotNull(zstdTar.Patterns);
        Assert.Equal(["*.zrus", "*.tar.zstd", "*.tzstd"], zstdTar.Patterns);

        // 3. Dedicated Zstandard Compressed Files category
        var zstFile = filters.FirstOrDefault(f => f.Name.StartsWith("Zstandard Compressed Files"));
        Assert.NotNull(zstFile);
        Assert.NotNull(zstFile.Patterns);
        Assert.Equal(["*.zst"], zstFile.Patterns);
    }

    [Fact]
    public async Task ExecuteCompressAsync_PassesStagedSourcesAndActiveExclusionsToRequest()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mw_compress_test_" + Guid.NewGuid().ToString("N"));
        var subDir = Path.Combine(tempDir, "ignore_me");
        Directory.CreateDirectory(subDir);
        var file1 = Path.Combine(tempDir, "keep.txt");
        var file2 = Path.Combine(subDir, "skip.txt");
        var tempDest = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zrus");

        File.WriteAllText(file1, "keep");
        File.WriteAllText(file2, "skip");

        try
        {
            var fakeEngine = new FakeArchiveEngine();
            var vm = new MainWindowViewModel(fakeEngine);

            // Stage folder
            vm.Settings.StageSources([tempDir]);
            vm.Settings.DestinationPath = tempDest;

            // Exclude the sub-directory
            var root = vm.Settings.StagedItems[0];
            var childToExclude = root.Children.FirstOrDefault(c => c.Name == "ignore_me");
            Assert.NotNull(childToExclude);
            vm.Settings.ToggleExclusionCommand.Execute(childToExclude);

            Assert.NotEmpty(vm.Settings.ExclusionPaths);
            Assert.Contains(childToExclude.FullPath, vm.Settings.ExclusionPaths);

            await vm.ExecuteCompressAsync();

            Assert.NotNull(fakeEngine.LastCompressionRequest);
            Assert.Single(fakeEngine.LastCompressionRequest.SourcePaths);
            Assert.Equal(tempDir, fakeEngine.LastCompressionRequest.SourcePaths[0]);
            Assert.Equal(tempDest, fakeEngine.LastCompressionRequest.DestinationArchivePath);
            Assert.NotNull(fakeEngine.LastCompressionRequest.ExcludedPaths);
            Assert.Contains(childToExclude.FullPath, fakeEngine.LastCompressionRequest.ExcludedPaths);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (File.Exists(tempDest)) File.Delete(tempDest);
        }
    }

    [Fact]
    public async Task HandleDroppedPathsAsync_WhenCompressDialogAlreadyVisible_AppendsSources()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mw_drop_append_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var file1 = Path.Combine(tempDir, "first.txt");
        var file2 = Path.Combine(tempDir, "second.txt");
        File.WriteAllText(file1, "1");
        File.WriteAllText(file2, "2");

        try
        {
            var fakeEngine = new FakeArchiveEngine();
            var vm = new MainWindowViewModel(fakeEngine);

            // Drop first file -> opens dialog
            await vm.HandleDroppedPathsAsync([file1]);
            Assert.True(vm.IsCompressDialogVisible);
            Assert.Single(vm.Settings.StagedItems);

            // Drop second file while dialog is open -> appends to staged items
            await vm.HandleDroppedPathsAsync([file2]);
            Assert.True(vm.IsCompressDialogVisible);
            Assert.Equal(2, vm.Settings.StagedItems.Count);
            Assert.Equal(file1, vm.Settings.StagedItems[0].FullPath);
            Assert.Equal(file2, vm.Settings.StagedItems[1].FullPath);
            Assert.Equal(Path.Combine(tempDir, "Archive.zrus"), vm.Settings.DestinationPath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData("test.zrus", true)]
    [InlineData("test.zip", true)]
    [InlineData("test.tar.zstd", true)]
    [InlineData("test.tzstd", true)]
    [InlineData("test.rar", false)]
    [InlineData("test.7z", false)]
    [InlineData("test.gz", false)]
    [InlineData("test.tar.gz", false)]
    [InlineData("test.tgz", false)]
    [InlineData("test.zst", false)]
    public async Task CanAppendToArchive_ReflectsOpenArchiveMutablility(string archiveName, bool expectedCanAppend)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "append_gate_" + Guid.NewGuid().ToString("N") + "_" + archiveName);
        File.WriteAllBytes(tempFile, [0x01]);
        try
        {
            var fakeEngine = new FakeArchiveEngine();
            var vm = new MainWindowViewModel(fakeEngine);

            Assert.False(vm.CanAppendToArchive);

            await vm.OpenArchiveAsync(tempFile);
            Assert.Equal(expectedCanAppend, vm.CanAppendToArchive);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CanDeleteFromArchive_GatedOnOpenArchiveCanCompressAndSelection()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "del_gate_" + Guid.NewGuid().ToString("N") + ".zrus");
        File.WriteAllBytes(tempFile, [0x01]);
        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn = [new ArchiveEntry("file1.txt", 100, 50, null, false)]
            };
            var vm = new MainWindowViewModel(fakeEngine);

            Assert.False(vm.CanDeleteFromArchive);

            await vm.OpenArchiveAsync(tempFile);
            Assert.True(vm.HasOpenArchive);
            Assert.False(vm.CanDeleteFromArchive); // no item selected yet

            var item = vm.Browser.FindItemByPath("file1.txt");
            Assert.NotNull(item);
            vm.Browser.SelectedItem = item;
            Assert.True(vm.CanDeleteFromArchive);

            // Clearing selection
            vm.Browser.SelectedItem = null;
            Assert.False(vm.CanDeleteFromArchive);

            // Multi-selection
            vm.Browser.SetSelectedItems([item]);
            Assert.True(vm.CanDeleteFromArchive);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AppendFilesCommand_WhenPathsProvided_CallsEngineAppendAndRefreshesArchive()
    {
        var tempArchive = Path.Combine(Path.GetTempPath(), "test_append_" + Guid.NewGuid().ToString("N") + ".zrus");
        var tempFile = Path.Combine(Path.GetTempPath(), "file_to_add_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllBytes(tempArchive, [0x01]);
        File.WriteAllText(tempFile, "hello");

        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn = [new ArchiveEntry("existing.txt", 10, 5, null, false)]
            };
            var vm = new MainWindowViewModel(fakeEngine);
            await vm.OpenArchiveAsync(tempArchive);

            vm.RequestAppendSourcePaths = () => Task.FromResult<IReadOnlyList<string>?>([tempFile]);

            // Update EntriesToReturn so refresh reflects new file
            fakeEngine.OnAppend = () =>
            {
                fakeEngine.EntriesToReturn =
                [
                    new ArchiveEntry("existing.txt", 10, 5, null, false),
                    new ArchiveEntry("file_to_add.txt", 5, 5, null, false)
                ];
            };

            await vm.AppendFilesCommand.ExecuteAsync(null);

            Assert.NotNull(fakeEngine.LastAppendRequest);
            Assert.Equal(tempArchive, fakeEngine.LastAppendRequest.ArchivePath);
            Assert.Single(fakeEngine.LastAppendRequest.SourcePaths);
            Assert.Equal(tempFile, fakeEngine.LastAppendRequest.SourcePaths[0]);

            // Archive tree refreshed
            Assert.Equal(2, vm.Browser.TotalEntries);
            Assert.Contains("Added 1 file", vm.StatusText);
        }
        finally
        {
            if (File.Exists(tempArchive)) File.Delete(tempArchive);
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AppendFilesCommand_WhenPickerCancelled_DoesNotCallEngine()
    {
        var tempArchive = Path.Combine(Path.GetTempPath(), "test_append_cancel_" + Guid.NewGuid().ToString("N") + ".zrus");
        File.WriteAllBytes(tempArchive, [0x01]);

        try
        {
            var fakeEngine = new FakeArchiveEngine();
            var vm = new MainWindowViewModel(fakeEngine);
            await vm.OpenArchiveAsync(tempArchive);

            vm.RequestAppendSourcePaths = () => Task.FromResult<IReadOnlyList<string>?>(null);

            await vm.AppendFilesCommand.ExecuteAsync(null);

            Assert.Null(fakeEngine.LastAppendRequest);
        }
        finally
        {
            if (File.Exists(tempArchive)) File.Delete(tempArchive);
        }
    }

    [Fact]
    public async Task DeleteSelectedCommand_WhenConfirmed_CallsEngineDeleteAndRefreshesArchive()
    {
        var tempArchive = Path.Combine(Path.GetTempPath(), "test_del_" + Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(tempArchive, [0x01]);

        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn =
                [
                    new ArchiveEntry("fileA.txt", 100, 50, null, false),
                    new ArchiveEntry("fileB.txt", 200, 100, null, false)
                ]
            };
            var vm = new MainWindowViewModel(fakeEngine);
            await vm.OpenArchiveAsync(tempArchive);

            var itemA = vm.Browser.FindItemByPath("fileA.txt")!;
            vm.Browser.SelectedItem = itemA;

            int confirmedCount = 0;
            IReadOnlyList<string>? confirmedPaths = null;
            vm.ConfirmDeleteAsync = (count, paths) =>
            {
                confirmedCount = count;
                confirmedPaths = paths;
                return Task.FromResult(true);
            };

            fakeEngine.OnDelete = () =>
            {
                fakeEngine.EntriesToReturn = [new ArchiveEntry("fileB.txt", 200, 100, null, false)];
            };

            await vm.DeleteSelectedCommand.ExecuteAsync(null);

            Assert.Equal(1, confirmedCount);
            Assert.NotNull(confirmedPaths);
            Assert.Equal("fileA.txt", confirmedPaths[0]);

            Assert.NotNull(fakeEngine.LastDeleteRequest);
            Assert.Equal(tempArchive, fakeEngine.LastDeleteRequest.ArchivePath);
            Assert.Single(fakeEngine.LastDeleteRequest.EntryPaths);
            Assert.Equal("fileA.txt", fakeEngine.LastDeleteRequest.EntryPaths[0]);

            // Archive tree refreshed
            Assert.Equal(1, vm.Browser.TotalEntries);
            Assert.Contains("Deleted 1 entry", vm.StatusText);
        }
        finally
        {
            if (File.Exists(tempArchive)) File.Delete(tempArchive);
        }
    }

    [Fact]
    public async Task DeleteSelectedCommand_WhenCancelled_DoesNotCallEngine()
    {
        var tempArchive = Path.Combine(Path.GetTempPath(), "test_del_cancel_" + Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(tempArchive, [0x01]);

        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn = [new ArchiveEntry("fileA.txt", 100, 50, null, false)]
            };
            var vm = new MainWindowViewModel(fakeEngine);
            await vm.OpenArchiveAsync(tempArchive);

            var itemA = vm.Browser.FindItemByPath("fileA.txt")!;
            vm.Browser.SelectedItem = itemA;

            vm.ConfirmDeleteAsync = (count, paths) => Task.FromResult(false);

            await vm.DeleteSelectedCommand.ExecuteAsync(null);

            Assert.Null(fakeEngine.LastDeleteRequest);
        }
        finally
        {
            if (File.Exists(tempArchive)) File.Delete(tempArchive);
        }
    }

    [Fact]
    public async Task ExecuteAppendAsync_OnReadOnlyArchive_RejectsWithoutCallingEngine()
    {
        var tempArchive = Path.Combine(Path.GetTempPath(), "test_readonly_" + Guid.NewGuid().ToString("N") + ".7z");
        File.WriteAllBytes(tempArchive, [0x01]);

        try
        {
            var fakeEngine = new FakeArchiveEngine();
            var vm = new MainWindowViewModel(fakeEngine);
            await vm.OpenArchiveAsync(tempArchive);

            await vm.ExecuteAppendAsync(["foo.txt"]);

            Assert.Null(fakeEngine.LastAppendRequest);
            Assert.Contains("not supported", vm.StatusText);
        }
        finally
        {
            if (File.Exists(tempArchive)) File.Delete(tempArchive);
        }
    }

    [Fact]
    public async Task ExecuteDeleteEntriesAsync_OnReadOnlyArchive_RejectsWithoutCallingEngine()
    {
        var tempArchive = Path.Combine(Path.GetTempPath(), "test_readonly_del_" + Guid.NewGuid().ToString("N") + ".7z");
        File.WriteAllBytes(tempArchive, [0x01]);

        try
        {
            var fakeEngine = new FakeArchiveEngine();
            var vm = new MainWindowViewModel(fakeEngine);
            await vm.OpenArchiveAsync(tempArchive);

            await vm.ExecuteDeleteEntriesAsync(["foo.txt"]);

            Assert.Null(fakeEngine.LastDeleteRequest);
            Assert.Contains("not supported", vm.StatusText);
        }
        finally
        {
            if (File.Exists(tempArchive)) File.Delete(tempArchive);
        }
    }

    [Fact]
    public async Task ExecuteExtractItemsAsync_CallsEngineExtractWithEntryFilter()
    {
        var tempArchive = Path.Combine(Path.GetTempPath(), "test_extract_items_" + Guid.NewGuid().ToString("N") + ".zrus");
        File.WriteAllBytes(tempArchive, [0x01]);

        try
        {
            var fakeEngine = new FakeArchiveEngine
            {
                EntriesToReturn =
                [
                    new ArchiveEntry("a.txt", 10, 5, null, false),
                    new ArchiveEntry("b.txt", 20, 10, null, false)
                ]
            };
            var vm = new MainWindowViewModel(fakeEngine);
            await vm.OpenArchiveAsync(tempArchive);

            var itemA = vm.Browser.FindItemByPath("a.txt")!;
            var itemB = vm.Browser.FindItemByPath("b.txt")!;

            await vm.ExecuteExtractItemsAsync([itemA, itemB], "/tmp/extracted");

            Assert.NotNull(fakeEngine.LastExtractionRequest);
            Assert.Equal(tempArchive, fakeEngine.LastExtractionRequest.ArchivePath);
            Assert.Equal("/tmp/extracted", fakeEngine.LastExtractionRequest.DestinationDirectory);
            Assert.NotNull(fakeEngine.LastExtractionRequest.Entries);
            Assert.Equal(2, fakeEngine.LastExtractionRequest.Entries.Count);
            Assert.Contains("a.txt", fakeEngine.LastExtractionRequest.Entries);
            Assert.Contains("b.txt", fakeEngine.LastExtractionRequest.Entries);
        }
        finally
        {
            if (File.Exists(tempArchive)) File.Delete(tempArchive);
        }
    }
}


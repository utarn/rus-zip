using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using RusZip.Desktop.Models;
using RusZip.Desktop.Services;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class SingleInstanceCoordinatorTests : IDisposable
{
    private readonly List<string> _tempFilesToClean = [];

    public void Dispose()
    {
        foreach (var file in _tempFilesToClean)
        {
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch { /* Ignore */ }
            }
        }
    }

    private class FakeArchiveEngine : IArchiveEngine
    {
        public List<ArchiveEntry> EntriesToReturn { get; set; } = [];

        public Task CompressAsync(ArchiveCompressionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<AppendResult> AppendAsync(ArchiveAppendRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new AppendResult(true, request.ArchivePath, "zrus", 0, 0, 0, 0, 0, 0, 0, 1.0, 0));

        public Task<ArchiveDeleteResult> DeleteEntriesAsync(ArchiveDeleteRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new ArchiveDeleteResult(true, request.ArchivePath, 0, 0, 0, 0, 0));

        public Task<ExtractionResult> ExtractAsync(ArchiveExtractionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new ExtractionResult(100, 1, 1));

        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ArchiveEntry>>(EntriesToReturn);
    }

    [Fact]
    public async Task TrySendToExistingInstanceAsync_WhenNoServerRunning_ReturnsFalse()
    {
        var customId = $"test_noserver_{Guid.NewGuid():N}";
        await using var client = new SingleInstanceCoordinator(customId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var result = await client.TrySendToExistingInstanceAsync("test.zip", cts.Token);

        Assert.False(result);
    }

    [Fact]
    public async Task StartListening_And_TrySendToExistingInstanceAsync_TransmitsFilePathSuccessfully()
    {
        var customId = $"test_transmit_{Guid.NewGuid():N}";
        await using var server = new SingleInstanceCoordinator(customId);

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.StartListening(path => tcs.TrySetResult(path));
        Assert.True(server.IsListening);

        await using var client = new SingleInstanceCoordinator(customId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var sendResult = await client.TrySendToExistingInstanceAsync("/path/to/archive.zrus", cts.Token);
        Assert.True(sendResult);

        var receivedPath = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("/path/to/archive.zrus", receivedPath);
    }

    [Fact]
    public async Task StartListening_And_TrySendToExistingInstanceAsync_WithNullFilePath_SendsNull()
    {
        var customId = $"test_null_{Guid.NewGuid():N}";
        await using var server = new SingleInstanceCoordinator(customId);

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.StartListening(path => tcs.TrySetResult(path));

        await using var client = new SingleInstanceCoordinator(customId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var sendResult = await client.TrySendToExistingInstanceAsync(null, cts.Token);
        Assert.True(sendResult);

        var receivedPath = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(receivedPath);
    }

    [Fact]
    public async Task StartListening_And_TrySendToExistingInstanceAsync_WithUnicodeAndSpaces_PreservesContent()
    {
        var customId = $"test_unicode_{Guid.NewGuid():N}";
        await using var server = new SingleInstanceCoordinator(customId);

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.StartListening(path => tcs.TrySetResult(path));

        await using var client = new SingleInstanceCoordinator(customId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var expectedPath = "/tmp/тестовая папка/архив 📦.zrus";
        var sendResult = await client.TrySendToExistingInstanceAsync(expectedPath, cts.Token);
        Assert.True(sendResult);

        var receivedPath = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(expectedPath, receivedPath);
    }

    [Fact]
    public void Dispose_CleansUpUnixSocketFile()
    {
        var customId = $"test_cleanup_{Guid.NewGuid():N}";
        var server = new SingleInstanceCoordinator(customId);

        server.StartListening(_ => { });
        Assert.True(server.IsListening);

        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.Exists(server.SocketPath));
        }

        server.Dispose();

        Assert.False(server.IsListening);
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(File.Exists(server.SocketPath));
        }
    }

    [Fact]
    public async Task DisposeAsync_CleansUpUnixSocketFile()
    {
        var customId = $"test_cleanup_async_{Guid.NewGuid():N}";
        var server = new SingleInstanceCoordinator(customId);

        server.StartListening(_ => { });
        Assert.True(server.IsListening);

        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.Exists(server.SocketPath));
        }

        await server.DisposeAsync();

        Assert.False(server.IsListening);
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(File.Exists(server.SocketPath));
        }
    }

    [Fact]
    public async Task StartListening_WhenStaleSocketFileExists_CleansUpAndStartsSuccessfully()
    {
        var customId = $"test_stale_{Guid.NewGuid():N}";
        var coordinator = new SingleInstanceCoordinator(customId);

        if (!OperatingSystem.IsWindows())
        {
            // Simulate a stale socket file from a crashed process
            var dir = Path.GetDirectoryName(coordinator.SocketPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(coordinator.SocketPath, "stale content");
            Assert.True(File.Exists(coordinator.SocketPath));
        }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StartListening(path => tcs.TrySetResult(path));
        Assert.True(coordinator.IsListening);

        await using var client = new SingleInstanceCoordinator(customId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var sendResult = await client.TrySendToExistingInstanceAsync("recovered.zip", cts.Token);
        Assert.True(sendResult);

        var receivedPath = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("recovered.zip", receivedPath);

        coordinator.Dispose();
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(File.Exists(coordinator.SocketPath));
        }
    }

    [Fact]
    public async Task MultipleSequentialClients_AllReceivedSuccessfully()
    {
        var customId = $"test_multi_{Guid.NewGuid():N}";
        await using var server = new SingleInstanceCoordinator(customId);

        var receivedList = new List<string?>();
        var semaphore = new SemaphoreSlim(0);

        server.StartListening(path =>
        {
            lock (receivedList)
            {
                receivedList.Add(path);
            }
            semaphore.Release();
        });

        for (int i = 1; i <= 3; i++)
        {
            await using var client = new SingleInstanceCoordinator(customId);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var sendResult = await client.TrySendToExistingInstanceAsync($"archive_{i}.zip", cts.Token);
            Assert.True(sendResult);
            Assert.True(await semaphore.WaitAsync(TimeSpan.FromSeconds(3)));
        }

        lock (receivedList)
        {
            Assert.Equal(["archive_1.zip", "archive_2.zip", "archive_3.zip"], receivedList);
        }
    }

    [Theory]
    [InlineData(new string[] { "archive.zip" }, "archive.zip")]
    [InlineData(new string[] { "--theme", "dark", "archive.zip" }, "archive.zip")]
    [InlineData(new string[] { "--theme=dark", "/path/to/archive.zrus" }, "/path/to/archive.zrus")]
    [InlineData(new string[] { "\"quoted/path.zip\"" }, "quoted/path.zip")]
    [InlineData(new string[] { "'single_quoted.zip'" }, "single_quoted.zip")]
    [InlineData(new string[] { "--flag1", "--flag2" }, null)]
    [InlineData(new string[] { }, null)]
    [InlineData(null, null)]
    public void ExtractArchiveArgument_ParsesCorrectly(string[]? args, string? expected)
    {
        var result = Program.ExtractArchiveArgument(args);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void QuickExtractionFlags_BypassSingleInstanceIpc()
    {
        // Quick extraction commands must return non-null from QuickExtractCommandLineParser.Parse
        // so Program.Main will bypass SingleInstanceCoordinator.
        Assert.NotNull(QuickExtractCommandLineParser.Parse(["--extract-here", "archive.zip"]));
        Assert.NotNull(QuickExtractCommandLineParser.Parse(["--extract-to", "archive.zip"]));
        Assert.NotNull(QuickExtractCommandLineParser.Parse(["--extract-to-dir", "archive.zip"]));
        Assert.NotNull(QuickExtractCommandLineParser.Parse(["--extract-to-subfolder", "archive.zip"]));

        // Regular archive open arguments must return null so Program.Main uses SingleInstanceCoordinator
        Assert.Null(QuickExtractCommandLineParser.Parse(["archive.zip"]));
        Assert.Null(QuickExtractCommandLineParser.Parse(["/path/to/my_archive.zrus"]));
        Assert.Null(QuickExtractCommandLineParser.Parse([]));
        Assert.Null(QuickExtractCommandLineParser.Parse(null));
    }

    [AvaloniaFact]
    public async Task MainWindow_FocusRestoration_OnIpcMessage()
    {
        var tempFile = Path.GetTempFileName();
        _tempFilesToClean.Add(tempFile);

        var fakeEngine = new FakeArchiveEngine
        {
            EntriesToReturn =
            [
                new ArchiveEntry("file1.txt", 100, 50, DateTimeOffset.UtcNow, false)
            ]
        };

        var vm = new MainWindowViewModel(fakeEngine);
        var window = new MainWindow
        {
            DataContext = vm
        };

        window.WindowState = WindowState.Minimized;
        Assert.Equal(WindowState.Minimized, window.WindowState);

        // Simulate IPC message handling logic from App.axaml.cs
        Action<string?> handleIpcMessage = receivedPath =>
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Activate();

            if (!string.IsNullOrEmpty(receivedPath) && File.Exists(receivedPath))
            {
                _ = vm.OpenArchiveAsync(receivedPath);
            }
        };

        handleIpcMessage(tempFile);

        Assert.Equal(WindowState.Normal, window.WindowState);

        // Wait for archive opening to complete
        for (int i = 0; i < 20; i++)
        {
            if (vm.HasOpenArchive) break;
            await Task.Delay(50);
        }

        Assert.True(vm.HasOpenArchive);
        Assert.Equal(tempFile, vm.Browser.LoadedArchivePath);
        Assert.Single(vm.Browser.RootItems);
    }
}

using System.Text.Json;
using RusZip.Desktop.Services;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class RecentArchivesServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _storageFile;

    public RecentArchivesServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ruszip-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _storageFile = Path.Combine(_tempDirectory, "recent-archives.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignored
            }
        }
    }

    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_InitializesEmptyList()
    {
        var service = new JsonRecentArchivesService(_storageFile);
        await service.LoadAsync();

        Assert.Empty(service.RecentPaths);
    }

    [Fact]
    public async Task AddRecentPathAsync_PrependsAndDeduplicates()
    {
        var service = new JsonRecentArchivesService(_storageFile);
        await service.LoadAsync();

        var pathA = Path.Combine(_tempDirectory, "archiveA.zip");
        var pathB = Path.Combine(_tempDirectory, "archiveB.zrus");

        await service.AddRecentPathAsync(pathA);
        await service.AddRecentPathAsync(pathB);
        await service.AddRecentPathAsync(pathA);

        Assert.Equal(2, service.RecentPaths.Count);
        Assert.Equal(Path.GetFullPath(pathA), service.RecentPaths[0]);
        Assert.Equal(Path.GetFullPath(pathB), service.RecentPaths[1]);
    }

    [Fact]
    public async Task AddRecentPathAsync_CapsAtMaxCapacity10()
    {
        var service = new JsonRecentArchivesService(_storageFile);
        await service.LoadAsync();

        for (int i = 1; i <= 15; i++)
        {
            var p = Path.Combine(_tempDirectory, $"archive_{i:D2}.zip");
            await service.AddRecentPathAsync(p);
        }

        Assert.Equal(JsonRecentArchivesService.MaxCapacity, service.RecentPaths.Count);
        Assert.Equal(10, service.RecentPaths.Count);
        Assert.Equal(Path.GetFullPath(Path.Combine(_tempDirectory, "archive_15.zip")), service.RecentPaths[0]);
        Assert.Equal(Path.GetFullPath(Path.Combine(_tempDirectory, "archive_06.zip")), service.RecentPaths[9]);
    }

    [Fact]
    public async Task RemoveRecentPathAsync_RemovesSpecifiedPathAndPersists()
    {
        var service = new JsonRecentArchivesService(_storageFile);
        await service.LoadAsync();

        var pathA = Path.Combine(_tempDirectory, "archiveA.zip");
        var pathB = Path.Combine(_tempDirectory, "archiveB.zrus");

        await service.AddRecentPathAsync(pathA);
        await service.AddRecentPathAsync(pathB);

        await service.RemoveRecentPathAsync(pathA);

        Assert.Single(service.RecentPaths);
        Assert.Equal(Path.GetFullPath(pathB), service.RecentPaths[0]);

        // Verify persisted state by loading in new service instance
        var service2 = new JsonRecentArchivesService(_storageFile);
        await service2.LoadAsync();
        Assert.Single(service2.RecentPaths);
        Assert.Equal(Path.GetFullPath(pathB), service2.RecentPaths[0]);
    }

    [Fact]
    public async Task ClearRecentPathsAsync_ClearsAllAndPersists()
    {
        var service = new JsonRecentArchivesService(_storageFile);
        await service.LoadAsync();

        await service.AddRecentPathAsync(Path.Combine(_tempDirectory, "archive1.zip"));
        await service.AddRecentPathAsync(Path.Combine(_tempDirectory, "archive2.zip"));

        Assert.Equal(2, service.RecentPaths.Count);

        await service.ClearRecentPathsAsync();

        Assert.Empty(service.RecentPaths);

        var service2 = new JsonRecentArchivesService(_storageFile);
        await service2.LoadAsync();
        Assert.Empty(service2.RecentPaths);
    }

    [Fact]
    public async Task LoadAsync_WithCorruptedJson_RecoversGracefullyToEmpty()
    {
        await File.WriteAllTextAsync(_storageFile, "INVALID JSON CONTENT {{{");

        var service = new JsonRecentArchivesService(_storageFile);
        await service.LoadAsync();

        Assert.Empty(service.RecentPaths);
    }

    [Fact]
    public async Task RecentPathsChanged_FiresOnMutations()
    {
        var service = new JsonRecentArchivesService(_storageFile);
        int changeCount = 0;
        service.RecentPathsChanged += (_, _) => changeCount++;

        await service.AddRecentPathAsync(Path.Combine(_tempDirectory, "test.zip"));
        Assert.Equal(1, changeCount);

        await service.RemoveRecentPathAsync(Path.Combine(_tempDirectory, "test.zip"));
        Assert.Equal(2, changeCount);

        await service.ClearRecentPathsAsync();
        Assert.Equal(3, changeCount);
    }
}

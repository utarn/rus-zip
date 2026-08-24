using System.Text.Json;

namespace RusZip.Desktop.Services;

public sealed class JsonRecentArchivesService : IRecentArchivesService
{
    public const int MaxCapacity = 10;

    private readonly string _storagePath;
    private readonly List<string> _recentPaths = [];
    private readonly object _lock = new();

    public event EventHandler? RecentPathsChanged;

    public IReadOnlyList<string> RecentPaths
    {
        get
        {
            lock (_lock)
            {
                return _recentPaths.ToList();
            }
        }
    }

    public string StoragePath => _storagePath;

    public JsonRecentArchivesService(string? customStoragePath = null)
    {
        _storagePath = customStoragePath ?? GetDefaultStoragePath();
    }

    public static string GetDefaultStoragePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        return Path.Combine(appData, "rus-zip", "recent-archives.json");
    }

    public async Task LoadAsync()
    {
        List<string> loaded = [];

        if (File.Exists(_storagePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_storagePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var deserialized = JsonSerializer.Deserialize<List<string>>(json);
                    if (deserialized != null)
                    {
                        loaded = deserialized
                            .Where(p => !string.IsNullOrWhiteSpace(p))
                            .Distinct()
                            .Take(MaxCapacity)
                            .ToList();
                    }
                }
            }
            catch
            {
                // Gracefully fallback to empty on corrupted JSON
                loaded = [];
            }
        }

        lock (_lock)
        {
            _recentPaths.Clear();
            _recentPaths.AddRange(loaded);
        }

        RecentPathsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task AddRecentPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var fullPath = Path.GetFullPath(path);

        lock (_lock)
        {
            _recentPaths.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
            _recentPaths.Insert(0, fullPath);

            while (_recentPaths.Count > MaxCapacity)
            {
                _recentPaths.RemoveAt(_recentPaths.Count - 1);
            }
        }

        await SaveAsync();
        RecentPathsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveRecentPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var fullPath = Path.GetFullPath(path);
        bool removed = false;

        lock (_lock)
        {
            var count = _recentPaths.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
            removed = count > 0;
        }

        if (removed)
        {
            await SaveAsync();
            RecentPathsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ClearRecentPathsAsync()
    {
        lock (_lock)
        {
            _recentPaths.Clear();
        }

        await SaveAsync();
        RecentPathsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveAsync()
    {
        List<string> snapshot;
        lock (_lock)
        {
            snapshot = _recentPaths.ToList();
        }

        try
        {
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            var tempPath = _storagePath + $".tmp.{Guid.NewGuid():N}";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, _storagePath, overwrite: true);
        }
        catch
        {
            // Logging / fault isolation
        }
    }
}

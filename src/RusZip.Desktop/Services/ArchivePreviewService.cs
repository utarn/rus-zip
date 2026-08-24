using System.Diagnostics;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;

namespace RusZip.Desktop.Services;

public sealed class ArchivePreviewService : IArchivePreviewService
{
    private readonly IArchiveEngine _engine;
    private readonly HashSet<string> _activeDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly Func<string, Process?> _processLauncher;

    public IReadOnlyCollection<string> ActivePreviewDirectories
    {
        get
        {
            lock (_lock)
            {
                return _activeDirectories.ToList();
            }
        }
    }

    public ArchivePreviewService(IArchiveEngine engine, Func<string, Process?>? processLauncher = null)
    {
        _engine = engine;
        _processLauncher = processLauncher ?? LaunchDefaultViewer;
    }

    public static Process? LaunchDefaultViewer(string filePath)
    {
        try
        {
            var psi = new ProcessStartInfo(filePath)
            {
                UseShellExecute = true
            };
            return Process.Start(psi);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> ExtractPreviewAsync(string archivePath, string relativeEntryPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            throw new FileNotFoundException($"Archive not found: {archivePath}", archivePath);
        }

        var sessionGuid = Guid.NewGuid().ToString("N");
        var baseTempDir = Path.Combine(Path.GetTempPath(), "rus-zip-preview", sessionGuid);
        Directory.CreateDirectory(baseTempDir);

        lock (_lock)
        {
            _activeDirectories.Add(baseTempDir);
        }

        var req = new ArchiveExtractionRequest(
            archivePath,
            baseTempDir,
            Overwrite: true,
            Entries: [relativeEntryPath]
        );

        await _engine.ExtractAsync(req, progress: null, ct);

        var normalized = relativeEntryPath.Replace('\\', '/').TrimStart('/');
        var targetFile = Path.Combine(baseTempDir, normalized.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(targetFile))
        {
            var directFile = Path.Combine(baseTempDir, Path.GetFileName(normalized));
            if (File.Exists(directFile))
            {
                return directFile;
            }
        }

        return targetFile;
    }

    public async Task PreviewEntryAsync(string archivePath, string relativeEntryPath, CancellationToken ct = default)
    {
        var extractedPath = await ExtractPreviewAsync(archivePath, relativeEntryPath, ct);
        if (File.Exists(extractedPath))
        {
            _processLauncher(extractedPath);
        }
    }

    public async Task CleanupAsync()
    {
        List<string> dirsToClean;
        lock (_lock)
        {
            dirsToClean = _activeDirectories.ToList();
            _activeDirectories.Clear();
        }

        await Task.Run(() =>
        {
            foreach (var dir in dirsToClean)
            {
                if (Directory.Exists(dir))
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                    catch
                    {
                        // Best-effort cleanup
                    }
                }
            }
        });
    }

    public void Dispose()
    {
        CleanupAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        await CleanupAsync();
    }
}

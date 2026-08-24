using System.Text;
using RusZip.Core.Models;
using RusZip.Desktop.Models;

namespace RusZip.Desktop.Services;

/// <summary>
/// Manages Linux FreeDesktop file associations, .desktop file generation with QuickExtract actions, and xdg-mime integration.
/// </summary>
public sealed class LinuxAssociationService : IFileAssociationService
{
    private readonly string _desktopFilePath;
    private readonly string _mimeappsFilePath;
    private readonly string _executablePath;
    private readonly Func<string, string[], Task<int>>? _commandRunner;

    public static readonly IReadOnlyList<string> ManagedExtensions =
    [
        ".zrus",
        ".tar.zstd",
        ".tzstd",
        ".zst",
        ".zip",
        ".tar.gz",
        ".tgz",
        ".7z",
        ".rar",
        ".gz"
    ];

    public static readonly IReadOnlyDictionary<string, string> ExtensionToMimeType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { ".zrus", "application/x-zstd-tar" },
        { ".tar.zstd", "application/x-zstd-tar" },
        { ".tzstd", "application/x-zstd-tar" },
        { ".zst", "application/zstd" },
        { ".zip", "application/zip" },
        { ".tar.gz", "application/gzip" },
        { ".tgz", "application/gzip" },
        { ".7z", "application/x-7z-compressed" },
        { ".rar", "application/vnd.rar" },
        { ".gz", "application/gzip" }
    };

    public IReadOnlyList<string> SupportedExtensions => ManagedExtensions;

    public LinuxAssociationService(
        string? desktopFilePath = null,
        string? mimeappsFilePath = null,
        string? executablePath = null,
        Func<string, string[], Task<int>>? commandRunner = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _desktopFilePath = desktopFilePath ?? Path.Combine(home, ".local", "share", "applications", "rus-zip.desktop");
        _mimeappsFilePath = mimeappsFilePath ?? Path.Combine(home, ".config", "mimeapps.list");
        _executablePath = executablePath ?? Environment.ProcessPath ?? "rus-zip";
        _commandRunner = commandRunner;
    }

    public static readonly IReadOnlyList<string> CanonicalMimeTypes =
    [
        "application/zip",
        "application/x-tar",
        "application/gzip",
        "application/x-7z-compressed",
        "application/vnd.rar",
        "application/x-zstd-tar",
        "application/zstd"
    ];

    public static string GenerateDesktopFileContent(string executablePath, IEnumerable<string>? extensions = null)
    {
        var exts = extensions?.ToList() ?? ManagedExtensions.ToList();
        var mimeList = new List<string>(CanonicalMimeTypes);

        foreach (var ext in exts)
        {
            if (ExtensionToMimeType.TryGetValue(ext, out var mime) && !mimeList.Contains(mime, StringComparer.OrdinalIgnoreCase))
            {
                mimeList.Add(mime);
            }
        }

        var mimeTypeStr = string.Join(";", mimeList) + ";";

        var sb = new StringBuilder();
        sb.AppendLine("[Desktop Entry]");
        sb.AppendLine("Type=Application");
        sb.AppendLine($"Name={AppBranding.DisplayName}");
        sb.AppendLine($"Exec={executablePath} %F");
        sb.AppendLine("Icon=rus-zip");
        sb.AppendLine($"MimeType={mimeTypeStr}");
        sb.AppendLine("Actions=ExtractHere;ExtractTo;ExtractToSubfolder;");
        sb.AppendLine();
        sb.AppendLine("[Desktop Action ExtractHere]");
        sb.AppendLine("Name=Extract here");
        sb.AppendLine($"Exec={executablePath} --extract-here %f");
        sb.AppendLine();
        sb.AppendLine("[Desktop Action ExtractTo]");
        sb.AppendLine("Name=Extract to...");
        sb.AppendLine($"Exec={executablePath} --extract-to %f");
        sb.AppendLine();
        sb.AppendLine("[Desktop Action ExtractToSubfolder]");
        sb.AppendLine("Name=Extract to subfolder");
        sb.AppendLine($"Exec={executablePath} --extract-to-dir %f");

        return sb.ToString();
    }

    public Task<IReadOnlyList<FileAssociationInfo>> GetAssociationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<FileAssociationInfo>();
        var desktopFileExists = File.Exists(_desktopFilePath);
        var mimeMap = ReadMimeAppsList();

        foreach (var ext in ManagedExtensions)
        {
            var isDetected = ArchiveFormatRegistry.TryDetect(ext, out var descriptor);
            var displayName = descriptor?.DisplayName ?? $"{ext.ToUpperInvariant()} Archive ({ext})";
            var mime = ExtensionToMimeType.GetValueOrDefault(ext, "application/octet-stream");

            bool isAssociated = false;
            string? currentHandler = null;

            if (desktopFileExists)
            {
                if (mimeMap.TryGetValue(mime, out var handler))
                {
                    if (handler.Equals("rus-zip.desktop", StringComparison.OrdinalIgnoreCase) ||
                        handler.StartsWith("rus-zip", StringComparison.OrdinalIgnoreCase))
                    {
                        isAssociated = true;
                        currentHandler = AppBranding.DisplayName;
                    }
                    else
                    {
                        isAssociated = false;
                        currentHandler = handler.Replace(".desktop", "");
                    }
                }
            }

            result.Add(new FileAssociationInfo(ext, displayName, isAssociated, currentHandler));
        }

        return Task.FromResult<IReadOnlyList<FileAssociationInfo>>(result);
    }

    public async Task<bool> AreAllFormatsAssociatedAsync(CancellationToken cancellationToken = default)
    {
        var list = await GetAssociationsAsync(cancellationToken);
        return list.Count > 0 && list.All(a => a.IsAssociated);
    }

    public async Task<bool> IsFormatAssociatedAsync(string extension, CancellationToken cancellationToken = default)
    {
        var list = await GetAssociationsAsync(cancellationToken);
        var item = list.FirstOrDefault(a => a.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase));
        return item?.IsAssociated ?? false;
    }

    public async Task RegisterAssociationsAsync(IEnumerable<string> extensions, CancellationToken cancellationToken = default)
    {
        var extList = extensions.ToList();
        var desktopContent = GenerateDesktopFileContent(_executablePath, extList);

        var desktopDir = Path.GetDirectoryName(_desktopFilePath);
        if (!string.IsNullOrEmpty(desktopDir) && !Directory.Exists(desktopDir))
        {
            Directory.CreateDirectory(desktopDir);
        }
        await File.WriteAllTextAsync(_desktopFilePath, desktopContent, cancellationToken);

        var mimeTypesToRegister = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in extList)
        {
            if (ExtensionToMimeType.TryGetValue(ext, out var mime))
            {
                mimeTypesToRegister.Add(mime);
            }
        }

        // Update ~/.config/mimeapps.list
        UpdateMimeAppsList(mimeTypesToRegister, "rus-zip.desktop");

        // Run xdg-mime default rus-zip.desktop <mimetypes>
        if (_commandRunner != null)
        {
            foreach (var mime in mimeTypesToRegister)
            {
                await _commandRunner("xdg-mime", ["default", "rus-zip.desktop", mime]);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                foreach (var mime in mimeTypesToRegister)
                {
                    using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "xdg-mime",
                        Arguments = $"default rus-zip.desktop {mime}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (proc != null)
                    {
                        await proc.WaitForExitAsync(cancellationToken);
                    }
                }
            }
            catch
            {
                // Fallback to file-based registration in mimeapps.list if xdg-mime utility is missing
            }
        }
    }

    public Task RegisterDefaultAssociationsAsync(CancellationToken cancellationToken = default)
    {
        return RegisterAssociationsAsync(ManagedExtensions, cancellationToken);
    }

    public Task RemoveAssociationsAsync(IEnumerable<string> extensions, CancellationToken cancellationToken = default)
    {
        var mimeTypesToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in extensions)
        {
            if (ExtensionToMimeType.TryGetValue(ext, out var mime))
            {
                mimeTypesToRemove.Add(mime);
            }
        }

        RemoveFromMimeAppsList(mimeTypesToRemove, "rus-zip.desktop");
        return Task.CompletedTask;
    }

    private Dictionary<string, string> ReadMimeAppsList()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_mimeappsFilePath))
        {
            return result;
        }

        try
        {
            var lines = File.ReadAllLines(_mimeappsFilePath);
            bool inDefaultSection = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    inDefaultSection = trimmed.Equals("[Default Applications]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inDefaultSection && trimmed.Contains('='))
                {
                    var parts = trimmed.Split('=', 2);
                    var mime = parts[0].Trim();
                    var handler = parts[1].Trim().TrimEnd(';');
                    if (!string.IsNullOrEmpty(mime) && !string.IsNullOrEmpty(handler))
                    {
                        result[mime] = handler;
                    }
                }
            }
        }
        catch
        {
            // Ignore read errors
        }

        return result;
    }

    private void UpdateMimeAppsList(IEnumerable<string> mimeTypes, string handlerDesktopFile)
    {
        try
        {
            var dir = Path.GetDirectoryName(_mimeappsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var lines = File.Exists(_mimeappsFilePath) ? File.ReadAllLines(_mimeappsFilePath).ToList() : [];
            var updatedLines = new List<string>();

            int defaultAppsIndex = -1;
            int addedAssocIndex = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.Equals("[Default Applications]", StringComparison.OrdinalIgnoreCase))
                {
                    defaultAppsIndex = i;
                }
                else if (trimmed.Equals("[Added Associations]", StringComparison.OrdinalIgnoreCase))
                {
                    addedAssocIndex = i;
                }
            }

            // Create dictionary of existing default associations
            var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (defaultAppsIndex >= 0)
            {
                for (int i = defaultAppsIndex + 1; i < lines.Count; i++)
                {
                    var line = lines[i].Trim();
                    if (line.StartsWith('[')) break;
                    if (line.Contains('='))
                    {
                        var parts = line.Split('=', 2);
                        defaults[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }

            foreach (var mime in mimeTypes)
            {
                defaults[mime] = handlerDesktopFile;
            }

            // Reconstruct file
            updatedLines.Add("[Default Applications]");
            foreach (var kvp in defaults)
            {
                updatedLines.Add($"{kvp.Key}={kvp.Value}");
            }
            updatedLines.Add(string.Empty);
            updatedLines.Add("[Added Associations]");
            foreach (var kvp in defaults)
            {
                updatedLines.Add($"{kvp.Key}={kvp.Value};");
            }

            File.WriteAllLines(_mimeappsFilePath, updatedLines);
        }
        catch
        {
            // Ignore file write errors
        }
    }

    private void RemoveFromMimeAppsList(IEnumerable<string> mimeTypes, string handlerDesktopFile)
    {
        if (!File.Exists(_mimeappsFilePath)) return;

        try
        {
            var mimeSet = mimeTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(_mimeappsFilePath);
            var newLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Contains('='))
                {
                    var parts = trimmed.Split('=', 2);
                    var mime = parts[0].Trim();
                    var handler = parts[1].Trim().TrimEnd(';');
                    if (mimeSet.Contains(mime) && handler.Equals(handlerDesktopFile, StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Remove entry
                    }
                }
                newLines.Add(line);
            }

            File.WriteAllLines(_mimeappsFilePath, newLines);
        }
        catch
        {
            // Ignore file write errors
        }
    }
}

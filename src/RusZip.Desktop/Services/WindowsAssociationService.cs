using RusZip.Core.Models;
using RusZip.Desktop.Models;

namespace RusZip.Desktop.Services;

/// <summary>
/// Manages Windows file associations, ProgIDs, SystemFileAssociations shell verbs, and RegisteredApplications.
/// </summary>
public sealed class WindowsAssociationService : IFileAssociationService
{
    private readonly IWindowsRegistry _registry;
    private readonly string _executablePath;

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

    public IReadOnlyList<string> SupportedExtensions => ManagedExtensions;

    public WindowsAssociationService(IWindowsRegistry? registry = null, string? executablePath = null)
    {
        _registry = registry ?? (OperatingSystem.IsWindows() ? new SystemWindowsRegistry() : new InMemoryWindowsRegistry());
        _executablePath = executablePath ?? Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rus-zip.exe");
    }

    public static string GetProgId(string extension)
    {
        var cleaned = extension.TrimStart('.').Replace(".", "");
        return $"RusZip.{cleaned}";
    }

    public Task<IReadOnlyList<FileAssociationInfo>> GetAssociationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<FileAssociationInfo>();

        foreach (var ext in ManagedExtensions)
        {
            var isDetected = ArchiveFormatRegistry.TryDetect(ext, out var descriptor);
            var displayName = descriptor?.DisplayName ?? $"{ext.ToUpperInvariant()} Archive ({ext})";
            var progId = GetProgId(ext);

            var currentProgId = _registry.GetValue("HKEY_CURRENT_USER", $@"Software\Classes\{ext}");
            var extractHereCmd = _registry.GetValue("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{ext}\shell\RusZip.ExtractHere\command");

            bool isAssociated = false;
            string? currentHandler = null;

            if (string.Equals(currentProgId, progId, StringComparison.OrdinalIgnoreCase))
            {
                isAssociated = true;
                currentHandler = "RusZip";
            }
            else if (!string.IsNullOrEmpty(extractHereCmd) && extractHereCmd.Contains(_executablePath, StringComparison.OrdinalIgnoreCase))
            {
                isAssociated = true;
                currentHandler = "RusZip";
            }
            else if (!string.IsNullOrEmpty(currentProgId))
            {
                isAssociated = false;
                currentHandler = currentProgId;
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

    public Task RegisterAssociationsAsync(IEnumerable<string> extensions, CancellationToken cancellationToken = default)
    {
        var exeStr = $"\"{_executablePath}\"";

        // Register Capabilities and RegisteredApplications
        _registry.SetValue("HKEY_CURRENT_USER", @"Software\RusZip\Capabilities", "ApplicationName", "RusZip");
        _registry.SetValue("HKEY_CURRENT_USER", @"Software\RusZip\Capabilities", "ApplicationDescription", "rus-zip - High-Performance Cross-Platform Compression Suite");
        _registry.SetValue("HKEY_CURRENT_USER", @"Software\RegisteredApplications", "RusZip", @"Software\RusZip\Capabilities");

        foreach (var ext in extensions)
        {
            var normalizedExt = ext.StartsWith('.') ? ext : $".{ext}";
            var progId = GetProgId(normalizedExt);
            var isDetected = ArchiveFormatRegistry.TryDetect(normalizedExt, out var descriptor);
            var displayName = descriptor?.DisplayName ?? $"{normalizedExt.ToUpperInvariant()} Archive ({normalizedExt})";

            // 1. ProgID key
            _registry.SetValue("HKEY_CURRENT_USER", $@"Software\Classes\{progId}", null, $"RusZip {displayName}");
            _registry.SetValue("HKEY_CURRENT_USER", $@"Software\Classes\{progId}\DefaultIcon", null, $"{exeStr},0");
            _registry.SetValue("HKEY_CURRENT_USER", $@"Software\Classes\{progId}\shell\open\command", null, $"{exeStr} \"%1\"");

            // 2. Extension key mapping
            _registry.SetValue("HKEY_CURRENT_USER", $@"Software\Classes\{normalizedExt}", null, progId);
            _registry.SetValue("HKEY_CURRENT_USER", $@"Software\Classes\{normalizedExt}\OpenWithProgids", progId, string.Empty);

            // 3. SystemFileAssociations shell verbs
            // Extract here
            _registry.SetValue("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{normalizedExt}\shell\RusZip.ExtractHere", null, "Extract here");
            _registry.SetValue("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{normalizedExt}\shell\RusZip.ExtractHere", "Icon", _executablePath);
            _registry.SetValue("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{normalizedExt}\shell\RusZip.ExtractHere\command", null, $"{exeStr} --extract-here \"%1\"");

            // Extract to...
            _registry.SetValue("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{normalizedExt}\shell\RusZip.ExtractTo", null, "Extract to...");
            _registry.SetValue("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{normalizedExt}\shell\RusZip.ExtractTo", "Icon", _executablePath);
            _registry.SetValue("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{normalizedExt}\shell\RusZip.ExtractTo\command", null, $"{exeStr} --extract-to \"%1\"");

            // Extract to subfolder
            _registry.SetValue("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{normalizedExt}\shell\RusZip.ExtractToSubfolder", null, "Extract to subfolder");
            _registry.SetValue("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{normalizedExt}\shell\RusZip.ExtractToSubfolder", "Icon", _executablePath);
            _registry.SetValue("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{normalizedExt}\shell\RusZip.ExtractToSubfolder\command", null, $"{exeStr} --extract-to-dir \"%1\"");

            // 4. Register in Capabilities\FileAssociations
            _registry.SetValue("HKEY_CURRENT_USER", @"Software\RusZip\Capabilities\FileAssociations", normalizedExt, progId);
        }

        return Task.CompletedTask;
    }

    public Task RegisterDefaultAssociationsAsync(CancellationToken cancellationToken = default)
    {
        return RegisterAssociationsAsync(ManagedExtensions, cancellationToken);
    }

    public Task RemoveAssociationsAsync(IEnumerable<string> extensions, CancellationToken cancellationToken = default)
    {
        foreach (var ext in extensions)
        {
            var normalizedExt = ext.StartsWith('.') ? ext : $".{ext}";
            var progId = GetProgId(normalizedExt);

            _registry.DeleteSubKeyTree("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{normalizedExt}\shell\RusZip.ExtractHere");
            _registry.DeleteSubKeyTree("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{normalizedExt}\shell\RusZip.ExtractTo");
            _registry.DeleteSubKeyTree("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{normalizedExt}\shell\RusZip.ExtractToSubfolder");

            var currentProgId = _registry.GetValue("HKEY_CURRENT_USER", $@"Software\Classes\{normalizedExt}");
            if (string.Equals(currentProgId, progId, StringComparison.OrdinalIgnoreCase))
            {
                _registry.DeleteValue("HKEY_CURRENT_USER", $@"Software\Classes\{normalizedExt}", string.Empty);
            }
            _registry.DeleteValue("HKEY_CURRENT_USER", $@"Software\Classes\{normalizedExt}\OpenWithProgids", progId);

            _registry.DeleteSubKeyTree("HKEY_CURRENT_USER", $@"Software\Classes\{progId}");
            _registry.DeleteValue("HKEY_CURRENT_USER", @"Software\RusZip\Capabilities\FileAssociations", normalizedExt);
        }

        return Task.CompletedTask;
    }
}

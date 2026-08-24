using System.Text;
using RusZip.Core.Models;
using RusZip.Desktop.Models;

namespace RusZip.Desktop.Services;

/// <summary>
/// Manages macOS LaunchServices default handlers, UTIs, and CFBundleDocumentTypes configuration.
/// </summary>
public sealed class MacAssociationService : IFileAssociationService
{
    private readonly string _bundleIdentifier;
    private readonly string _executablePath;
    private readonly Func<string, string[], Task<int>>? _commandRunner;
    private readonly Dictionary<string, string> _registeredHandlers = new(StringComparer.OrdinalIgnoreCase);

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

    public static readonly IReadOnlyDictionary<string, string> ExtensionToUti = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { ".zrus", "org.zstd.tar-archive" },
        { ".tar.zstd", "org.zstd.tar-archive" },
        { ".tzstd", "org.zstd.tar-archive" },
        { ".zst", "org.zstd.zstandard-archive" },
        { ".zip", "public.zip-archive" },
        { ".tar.gz", "org.gnu.gnu-zip-tar-archive" },
        { ".tgz", "org.gnu.gnu-zip-tar-archive" },
        { ".7z", "org.7-zip.7-zip-archive" },
        { ".rar", "com.rarlab.rar-archive" },
        { ".gz", "org.gnu.gnu-zip-archive" }
    };

    public IReadOnlyList<string> SupportedExtensions => ManagedExtensions;

    public MacAssociationService(
        string? bundleIdentifier = null,
        string? executablePath = null,
        Func<string, string[], Task<int>>? commandRunner = null)
    {
        _bundleIdentifier = bundleIdentifier ?? "com.ruszip.desktop";
        _executablePath = executablePath ?? Environment.ProcessPath ?? "rus-zip";
        _commandRunner = commandRunner;
    }

    public static string GenerateDocumentTypesPlist(IEnumerable<string>? extensions = null)
    {
        var exts = extensions?.ToList() ?? ManagedExtensions.ToList();
        var sb = new StringBuilder();
        sb.AppendLine("    <key>CFBundleDocumentTypes</key>");
        sb.AppendLine("    <array>");

        foreach (var ext in exts)
        {
            var isDetected = ArchiveFormatRegistry.TryDetect(ext, out var desc);
            var name = desc != null ? $"{AppBranding.DisplayName} {desc.DisplayName}" : $"{AppBranding.DisplayName} {ext} Archive";
            var uti = ExtensionToUti.GetValueOrDefault(ext, "public.data");
            var cleanExt = ext.TrimStart('.');

            sb.AppendLine("        <dict>");
            sb.AppendLine($"            <key>CFBundleTypeName</key>");
            sb.AppendLine($"            <string>{name}</string>");
            sb.AppendLine($"            <key>CFBundleTypeRole</key>");
            sb.AppendLine($"            <string>Viewer</string>");
            sb.AppendLine($"            <key>LSHandlerRank</key>");
            sb.AppendLine($"            <string>{(ext.Equals(".zrus", StringComparison.OrdinalIgnoreCase) ? "Owner" : "Default")}</string>");
            sb.AppendLine($"            <key>LSItemContentTypes</key>");
            sb.AppendLine($"            <array>");
            sb.AppendLine($"                <string>{uti}</string>");
            sb.AppendLine($"            </array>");
            sb.AppendLine($"            <key>CFBundleTypeExtensions</key>");
            sb.AppendLine($"            <array>");
            sb.AppendLine($"                <string>{cleanExt}</string>");
            sb.AppendLine($"            </array>");
            sb.AppendLine("        </dict>");
        }

        sb.AppendLine("    </array>");
        return sb.ToString();
    }

    public Task<IReadOnlyList<FileAssociationInfo>> GetAssociationsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<FileAssociationInfo>();

        foreach (var ext in ManagedExtensions)
        {
            var isDetected = ArchiveFormatRegistry.TryDetect(ext, out var descriptor);
            var displayName = descriptor?.DisplayName ?? $"{ext.ToUpperInvariant()} Archive ({ext})";
            var uti = ExtensionToUti.GetValueOrDefault(ext, "public.data");

            bool isAssociated = false;
            string? currentHandler = null;

            if (_registeredHandlers.TryGetValue(uti, out var handler))
            {
                if (handler.Equals(_bundleIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    isAssociated = true;
                    currentHandler = AppBranding.DisplayName;
                }
                else
                {
                    isAssociated = false;
                    currentHandler = handler;
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
        foreach (var ext in extensions)
        {
            if (ExtensionToUti.TryGetValue(ext, out var uti))
            {
                _registeredHandlers[uti] = _bundleIdentifier;

                if (_commandRunner != null)
                {
                    await _commandRunner("duti", ["-s", _bundleIdentifier, uti, "all"]);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    try
                    {
                        using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "duti",
                            Arguments = $"-s {_bundleIdentifier} {uti} all",
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
                    catch
                    {
                        // Fallback silently if duti CLI is not present
                    }
                }
            }
        }
    }

    public Task RegisterDefaultAssociationsAsync(CancellationToken cancellationToken = default)
    {
        return RegisterAssociationsAsync(ManagedExtensions, cancellationToken);
    }

    public Task RemoveAssociationsAsync(IEnumerable<string> extensions, CancellationToken cancellationToken = default)
    {
        foreach (var ext in extensions)
        {
            if (ExtensionToUti.TryGetValue(ext, out var uti))
            {
                _registeredHandlers.Remove(uti);
            }
        }

        return Task.CompletedTask;
    }
}

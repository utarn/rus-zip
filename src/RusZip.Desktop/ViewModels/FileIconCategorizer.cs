using RusZip.Core.Models;

namespace RusZip.Desktop.ViewModels;

/// <summary>
/// Presentation-layer file icon categorization (ADR-0005 note).
///
/// Format <em>capabilities</em> live exclusively in <see cref="ArchiveFormatRegistry"/>; this
/// type is a rendering concern and deliberately contains no detection or dispatch logic.
/// Archive-like rendering is derived from the registry first, then a small alias table covers
/// well-known archive file extensions the registry does not model (e.g. <c>.tar</c>, <c>.iso</c>).
/// That alias table is presentation aliasing only — it must never be consulted for format
/// decisions. Extensions the registry recognizes are always classified through the registry.
/// </summary>
public static class FileIconCategorizer
{
    // Presentation-only aliases for well-known archive file names outside the registry.
    // These carry NO capability claim: they exist so such files render with an archive icon
    // even though the Core registry cannot open them. Additions here are purely cosmetic.
    private static readonly HashSet<string> ArchiveExtensionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tar", ".bz2", ".xz", ".cab", ".iso", ".7zip", ".tbz2", ".txz"
    };

    /// <summary>
    /// Determines whether <paramref name="fileName"/> should render with an archive icon.
    /// Registry-recognized formats match first (covering multi-part extensions such as
    /// <c>.tar.gz</c> and <c>.tgz</c>); non-registry archive-like extensions fall back to the
    /// presentation alias table.
    /// </summary>
    public static bool IsArchiveFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (ArchiveFormatRegistry.TryDetect(fileName, out _))
        {
            return true;
        }

        return ArchiveExtensionAliases.Contains(Path.GetExtension(fileName));
    }

    public static string GetFileIcon(string fileName)
    {
        if (IsArchiveFile(fileName))
        {
            return "📦";
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".cs" or ".rs" or ".py" or ".js" or ".ts" or ".jsx" or ".tsx" or ".vue" or ".svelte" or ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".html" or ".css" or ".scss" or ".sass" or ".less" or ".sh" or ".bash" or ".zsh" or ".cpp" or ".c" or ".cc" or ".cxx" or ".h" or ".hpp" or ".hh" or ".go" or ".java" or ".kt" or ".kts" or ".swift" or ".php" or ".rb" or ".lua" or ".m" or ".mm" or ".scala" or ".sql" or ".ps1" or ".bat" or ".cmd" => "📝",
            ".png" or ".jpg" or ".jpeg" or ".svg" or ".webp" or ".gif" or ".bmp" or ".ico" or ".tiff" or ".tif" or ".heic" or ".avif" => "🖼️",
            ".pdf" or ".doc" or ".docx" or ".txt" or ".md" or ".rtf" or ".log" or ".csv" or ".odt" or ".xlsx" or ".xls" or ".pptx" or ".ppt" => "📄",
            ".exe" or ".dll" or ".so" or ".dylib" or ".bin" => "⚙️",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".mp4" or ".mkv" or ".avi" or ".mov" => "🎬",
            _ => "📄"
        };
    }

    public static string GetIconKey(string fileName, bool isDirectory = false)
    {
        if (isDirectory)
        {
            return "Icon.Folder";
        }

        if (IsArchiveFile(fileName))
        {
            return "Icon.FileArchive";
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".cs" or ".rs" or ".py" or ".js" or ".ts" or ".jsx" or ".tsx" or ".vue" or ".svelte" or
            ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".html" or ".css" or ".scss" or ".sass" or ".less" or
            ".sh" or ".bash" or ".zsh" or ".cpp" or ".c" or ".cc" or ".cxx" or ".h" or ".hpp" or ".hh" or
            ".go" or ".java" or ".kt" or ".kts" or ".swift" or ".php" or ".rb" or ".lua" or ".m" or ".mm" or
            ".scala" or ".sql" or ".ps1" or ".bat" or ".cmd" => "Icon.FileCode",

            ".txt" or ".md" or ".pdf" or ".doc" or ".docx" or ".rtf" or ".log" or ".csv" or ".odt" or
            ".xlsx" or ".xls" or ".pptx" or ".ppt" => "Icon.FileDoc",

            ".png" or ".jpg" or ".jpeg" or ".svg" or ".webp" or ".gif" or ".bmp" or ".ico" or
            ".tiff" or ".tif" or ".heic" or ".avif" => "Icon.FileImage",

            _ => "Icon.FileGeneric"
        };
    }
}

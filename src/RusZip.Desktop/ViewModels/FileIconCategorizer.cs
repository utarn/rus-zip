using RusZip.Core.Models;

namespace RusZip.Desktop.ViewModels;

public enum FileCategory
{
    All,
    Documents,
    Images,
    Code,
    Media,
    Archives
}

/// <summary>
/// Presentation-layer file icon categorization and category filtering.
/// </summary>
public static class FileIconCategorizer
{
    private static readonly HashSet<string> ArchiveExtensionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tar", ".bz2", ".xz", ".cab", ".iso", ".7zip", ".tbz2", ".txz"
    };

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".rs", ".py", ".js", ".ts", ".jsx", ".tsx", ".vue", ".svelte",
        ".json", ".xml", ".yaml", ".yml", ".toml", ".html", ".css", ".scss", ".sass", ".less",
        ".sh", ".bash", ".zsh", ".cpp", ".c", ".cc", ".cxx", ".h", ".hpp", ".hh",
        ".go", ".java", ".kt", ".kts", ".swift", ".php", ".rb", ".lua", ".m", ".mm",
        ".scala", ".sql", ".ps1", ".bat", ".cmd"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".txt", ".md", ".rtf", ".log", ".csv", ".odt",
        ".xlsx", ".xls", ".pptx", ".ppt"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".svg", ".webp", ".gif", ".bmp", ".ico",
        ".tiff", ".tif", ".heic", ".avif"
    };

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".ogg", ".mp4", ".mkv", ".avi", ".mov", ".aac", ".m4a", ".webm"
    };

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

    public static FileCategory GetFileCategory(string fileName, bool isDirectory = false)
    {
        if (isDirectory)
        {
            return FileCategory.All;
        }

        if (IsArchiveFile(fileName))
        {
            return FileCategory.Archives;
        }

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext))
        {
            return FileCategory.All;
        }

        if (CodeExtensions.Contains(ext)) return FileCategory.Code;
        if (DocumentExtensions.Contains(ext)) return FileCategory.Documents;
        if (ImageExtensions.Contains(ext)) return FileCategory.Images;
        if (MediaExtensions.Contains(ext)) return FileCategory.Media;

        return FileCategory.All;
    }

    public static string GetFileIcon(string fileName)
    {
        if (IsArchiveFile(fileName))
        {
            return "📦";
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (CodeExtensions.Contains(ext)) return "📝";
        if (ImageExtensions.Contains(ext)) return "🖼️";
        if (DocumentExtensions.Contains(ext)) return "📄";
        if (MediaExtensions.Contains(ext)) return "🎬";
        if (ext is ".exe" or ".dll" or ".so" or ".dylib" or ".bin") return "⚙️";

        return "📄";
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
        if (CodeExtensions.Contains(ext)) return "Icon.FileCode";
        if (DocumentExtensions.Contains(ext)) return "Icon.FileDoc";
        if (ImageExtensions.Contains(ext)) return "Icon.FileImage";

        return "Icon.FileGeneric";
    }
}

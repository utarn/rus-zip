namespace RusZip.Core.Models;

public enum ArchiveFormat
{
    Zrus,
    Zip,
    Rar,
    SevenZip,
    Gz,
    TarGz
}

public static class ArchiveFormatDetector
{
    public static ArchiveFormat DetectFromPath(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz"))
            return ArchiveFormat.TarGz;
        if (lower.EndsWith(".zrus"))
            return ArchiveFormat.Zrus;
        if (lower.EndsWith(".zip"))
            return ArchiveFormat.Zip;
        if (lower.EndsWith(".rar"))
            return ArchiveFormat.Rar;
        if (lower.EndsWith(".7z"))
            return ArchiveFormat.SevenZip;
        if (lower.EndsWith(".gz"))
            return ArchiveFormat.Gz;

        throw new NotSupportedException($"Unsupported archive extension for file '{path}'. Supported: .zrus, .zip, .rar, .7z, .gz, .tar.gz");
    }
}

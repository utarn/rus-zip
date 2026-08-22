namespace RusZip.Core.Models;

public sealed record ArchiveFormatDescriptor(
    ArchiveFormat Format,
    string DisplayName,
    string PrimaryExtension,
    IReadOnlyList<string> Extensions,
    bool CanCompress,
    bool CanDecompress,
    int MinCompressionLevel,
    int MaxCompressionLevel,
    int DefaultCompressionLevel,
    string MimeType,
    string CategoryDescription
)
{
    public bool MatchesExtension(string extensionOrPath)
    {
        if (string.IsNullOrWhiteSpace(extensionOrPath))
            return false;

        var clean = extensionOrPath.Trim();
        if (clean.StartsWith('.'))
        {
            return Extensions.Any(ext => ext.Equals(clean, StringComparison.OrdinalIgnoreCase));
        }

        return Extensions.Any(ext =>
            clean.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ||
            (!clean.Contains('/') && !clean.Contains('\\') && ext.TrimStart('.').Equals(clean, StringComparison.OrdinalIgnoreCase)));
    }
}

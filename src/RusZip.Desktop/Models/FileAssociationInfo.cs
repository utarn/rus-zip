namespace RusZip.Desktop.Models;

/// <summary>
/// Represents the OS file association status for a specific archive format extension.
/// </summary>
/// <param name="Extension">The primary file extension (e.g. ".zrus", ".zip", ".tar.gz").</param>
/// <param name="FormatDisplayName">The human-readable display name of the format.</param>
/// <param name="IsAssociated">True if rus-zip is registered as the default handler; otherwise false.</param>
/// <param name="CurrentHandler">The name or identifier of the current default application handler, if known.</param>
public sealed record FileAssociationInfo(
    string Extension,
    string FormatDisplayName,
    bool IsAssociated,
    string? CurrentHandler = null
);

using RusZip.Core.Models;
using RusZip.Desktop.Models;

namespace RusZip.Desktop.Services;

/// <summary>
/// Service managing OS-level file extension registrations, ProgIDs, MIME types,
/// and default application queries across Windows, Linux, and macOS.
/// </summary>
public interface IFileAssociationService
{
    /// <summary>
    /// The list of archive file extensions managed by the association service (.zrus, .zip, .tar.gz, etc.).
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Retrieves the current file association status and active handler for all supported formats.
    /// </summary>
    Task<IReadOnlyList<FileAssociationInfo>> GetAssociationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether all supported archive formats are currently associated with rus-zip.
    /// </summary>
    Task<bool> AreAllFormatsAssociatedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a specific archive format extension is associated with rus-zip.
    /// </summary>
    Task<bool> IsFormatAssociatedAsync(string extension, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers rus-zip as the default application and installs shell verbs for the specified extensions.
    /// </summary>
    Task RegisterAssociationsAsync(IEnumerable<string> extensions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers all supported archive extensions as default associations for rus-zip.
    /// </summary>
    Task RegisterDefaultAssociationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes rus-zip file associations and context menu verbs for the specified extensions.
    /// </summary>
    Task RemoveAssociationsAsync(IEnumerable<string> extensions, CancellationToken cancellationToken = default);
}

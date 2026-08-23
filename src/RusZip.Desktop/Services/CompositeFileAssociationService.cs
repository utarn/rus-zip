using RusZip.Desktop.Models;

namespace RusZip.Desktop.Services;

/// <summary>
/// Composite file association service that wraps an inner active platform implementation.
/// </summary>
public sealed class CompositeFileAssociationService : IFileAssociationService
{
    private readonly IFileAssociationService _activeService;

    public IReadOnlyList<string> SupportedExtensions => _activeService.SupportedExtensions;

    public CompositeFileAssociationService(IFileAssociationService activeService)
    {
        _activeService = activeService ?? throw new ArgumentNullException(nameof(activeService));
    }

    public Task<IReadOnlyList<FileAssociationInfo>> GetAssociationsAsync(CancellationToken cancellationToken = default)
        => _activeService.GetAssociationsAsync(cancellationToken);

    public Task<bool> AreAllFormatsAssociatedAsync(CancellationToken cancellationToken = default)
        => _activeService.AreAllFormatsAssociatedAsync(cancellationToken);

    public Task<bool> IsFormatAssociatedAsync(string extension, CancellationToken cancellationToken = default)
        => _activeService.IsFormatAssociatedAsync(extension, cancellationToken);

    public Task RegisterAssociationsAsync(IEnumerable<string> extensions, CancellationToken cancellationToken = default)
        => _activeService.RegisterAssociationsAsync(extensions, cancellationToken);

    public Task RegisterDefaultAssociationsAsync(CancellationToken cancellationToken = default)
        => _activeService.RegisterDefaultAssociationsAsync(cancellationToken);

    public Task RemoveAssociationsAsync(IEnumerable<string> extensions, CancellationToken cancellationToken = default)
        => _activeService.RemoveAssociationsAsync(extensions, cancellationToken);
}

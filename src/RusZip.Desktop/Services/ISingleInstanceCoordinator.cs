namespace RusZip.Desktop.Services;

/// <summary>
/// Coordinates single-instance application lifecycle and inter-process communication for desktop archive opening.
/// </summary>
public interface ISingleInstanceCoordinator : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Attempts to connect to an existing running application instance and send a file path to open.
    /// </summary>
    /// <param name="filePath">Archive file path to send, or null/empty to bring the existing window to front.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if successfully connected and delivered to an existing instance; otherwise <c>false</c>.</returns>
    Task<bool> TrySendToExistingInstanceAsync(string? filePath, CancellationToken ct = default);

    /// <summary>
    /// Starts listening for incoming IPC connections from secondary instances.
    /// </summary>
    /// <param name="onFileReceived">Action invoked with the received file path (or null if none) when a secondary instance connects.</param>
    void StartListening(Action<string?> onFileReceived);

    /// <summary>
    /// Stops listening and releases server resources.
    /// </summary>
    void StopListening();
}

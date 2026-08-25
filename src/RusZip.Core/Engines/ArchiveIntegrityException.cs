namespace RusZip.Core.Engines;

/// <summary>
/// Thrown when archive integrity verification fails during extraction or listing:
/// a zstd frame content checksum mismatch, a zip entry CRC-32 mismatch, or a structurally
/// unparseable central directory (an archive that declares entries but yields none).
/// An archiver must never report success for corrupt data.
/// </summary>
/// <remarks>
/// The CLI maps this dedicated type to <c>EXECUTION_ERROR</c> (exit 1).
/// </remarks>
public class ArchiveIntegrityException : Exception
{
    /// <summary>The archive entry whose integrity check failed, when known.</summary>
    public string? EntryName { get; }

    public ArchiveIntegrityException(string message, string? entryName = null, Exception? innerException = null)
        : base(message, innerException)
    {
        EntryName = entryName;
    }
}

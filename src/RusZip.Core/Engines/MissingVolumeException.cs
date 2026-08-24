namespace RusZip.Core.Engines;

/// <summary>
/// Thrown when a multi-volume archive is missing one or more sequential volume parts during extraction or inspection.
/// </summary>
public sealed class MissingVolumeException : ArchiveIntegrityException
{
    public string? ExpectedVolumePath { get; }
    public int ExpectedVolumeIndex { get; }

    public MissingVolumeException(string message, string? expectedVolumePath = null, int expectedVolumeIndex = 0, Exception? innerException = null)
        : base(message, expectedVolumePath, innerException)
    {
        ExpectedVolumePath = expectedVolumePath;
        ExpectedVolumeIndex = expectedVolumeIndex;
    }
}

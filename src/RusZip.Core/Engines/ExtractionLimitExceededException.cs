namespace RusZip.Core.Engines;

/// <summary>
/// Thrown by <see cref="SafeArchiveExtractor"/> when an extraction aborts because a
/// user-configured guardrail was exceeded. Limits are measured from actual streamed bytes
/// and processed entries — never from archive header metadata, which is spoofable.
/// </summary>
public sealed class ExtractionLimitExceededException : Exception
{
    public ExtractionLimitExceededException(string message) : base(message)
    {
    }

    public ExtractionLimitExceededException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

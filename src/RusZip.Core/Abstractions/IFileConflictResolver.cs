namespace RusZip.Core.Abstractions;

public enum FileConflictResolution
{
    Overwrite,
    OverwriteAll,
    Skip,
    SkipAll,
    Abort
}

public sealed record FileConflictContext(
    string TargetPath,
    string RelativeEntryPath,
    long EntryUncompressedSize,
    DateTimeOffset? EntryLastModified,
    long ExistingFileSize,
    DateTimeOffset ExistingLastModified
);

public interface IFileConflictResolver
{
    ValueTask<FileConflictResolution> ResolveConflictAsync(
        FileConflictContext context,
        CancellationToken cancellationToken = default);
}

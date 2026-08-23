namespace RusZip.Core.Abstractions;

public enum FileConflictResolution
{
    Abort = 0,
    Overwrite = 1,
    OverwriteAll = 2,
    Skip = 3,
    SkipAll = 4
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

public sealed class FixedPolicyConflictResolver : IFileConflictResolver
{
    public FileConflictResolution Resolution { get; }

    public FixedPolicyConflictResolver(FileConflictResolution resolution)
    {
        Resolution = resolution;
    }

    public static FixedPolicyConflictResolver Abort { get; } = new(FileConflictResolution.Abort);
    public static FixedPolicyConflictResolver Overwrite { get; } = new(FileConflictResolution.Overwrite);
    public static FixedPolicyConflictResolver OverwriteAll { get; } = new(FileConflictResolution.OverwriteAll);
    public static FixedPolicyConflictResolver Skip { get; } = new(FileConflictResolution.Skip);
    public static FixedPolicyConflictResolver SkipAll { get; } = new(FileConflictResolution.SkipAll);

    public ValueTask<FileConflictResolution> ResolveConflictAsync(
        FileConflictContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Resolution);
    }
}

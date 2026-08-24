namespace RusZip.Desktop.Services;

public interface IRecentArchivesService
{
    IReadOnlyList<string> RecentPaths { get; }
    event EventHandler? RecentPathsChanged;
    Task LoadAsync();
    Task AddRecentPathAsync(string path);
    Task RemoveRecentPathAsync(string path);
    Task ClearRecentPathsAsync();
}

using System.ComponentModel;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using RusZip.Desktop.Models;
using RusZip.Desktop.ViewModels;
using Xunit;

namespace RusZip.Desktop.Tests;

/// <summary>
/// End-to-end regression tests verifying the integrated Desktop application workflows:
/// Drag-and-drop empty state -> compression presets -> archive loading -> ProDataGrid projection ->
/// breadcrumbs drilldown & navigation -> item extraction -> theme toggling.
/// </summary>
public sealed class DesktopEndToEndRegressionTests : IDisposable
{
    private readonly string _tempDir;

    public DesktopEndToEndRegressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "desktop_e2e_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { /* Ignore */ }
        }
    }

    private class FakeArchiveEngine : IArchiveEngine
    {
        public List<ArchiveEntry> EntriesToReturn { get; set; } = [];
        public ArchiveCompressionRequest? LastCompressionRequest { get; private set; }
        public ArchiveExtractionRequest? LastExtractionRequest { get; private set; }

        public Task CompressAsync(ArchiveCompressionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            LastCompressionRequest = request;
            if (!string.IsNullOrEmpty(request.DestinationArchivePath))
            {
                var dir = Path.GetDirectoryName(request.DestinationArchivePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (!File.Exists(request.DestinationArchivePath))
                {
                    File.WriteAllBytes(request.DestinationArchivePath, [0x01]);
                }
            }
            progress?.Report(new ProgressReport(1000, 1000, "compress.zrus", 100.0, 1, 1));
            return Task.CompletedTask;
        }

        public Task<AppendResult> AppendAsync(ArchiveAppendRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            return Task.FromResult(new AppendResult(true, request.ArchivePath, "zrus", 0, 0, 0, 0, 0, 0, 0, 1.0, 0));
        }

        public Task<ArchiveDeleteResult> DeleteEntriesAsync(ArchiveDeleteRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            return Task.FromResult(new ArchiveDeleteResult(true, request.ArchivePath, 0, 0, 0, 0, 0));
        }

        public Task<ExtractionResult> ExtractAsync(ArchiveExtractionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            LastExtractionRequest = request;
            progress?.Report(new ProgressReport(1000, 1000, "extract.txt", 100.0, 1, 1));
            return Task.FromResult(new ExtractionResult(1000, 1, 1));
        }

        public Task<ArchiveTestResult> TestArchiveAsync(string archivePath, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            return Task.FromResult(new ArchiveTestResult(true, archivePath, "zrus", EntriesToReturn.Count, 1000, 50.0, TimeSpan.FromMilliseconds(20), []));
        }

        public Task<ArchiveTestResult> TestArchiveAsync(string archivePath, string? password, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            return Task.FromResult(new ArchiveTestResult(true, archivePath, "zrus", EntriesToReturn.Count, 1000, 50.0, TimeSpan.FromMilliseconds(20), []));
        }

        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<ArchiveEntry>>(EntriesToReturn);
        }

        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, string? password, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<ArchiveEntry>>(EntriesToReturn);
        }

        public Task<bool> IsEncryptedAsync(string archivePath, CancellationToken ct = default)
        {
            return Task.FromResult(false);
        }

        public Task<IReadOnlyList<string>> GetVolumePartsAsync(string archivePath, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([archivePath]);
    }

    [Fact]
    public async Task EndToEnd_DesktopFullLifecycle_EmptyState_Compress_Browse_Breadcrumbs_Extract()
    {
        // 1. Initial Empty State
        var fakeEngine = new FakeArchiveEngine();
        var mainWindowVm = new MainWindowViewModel(fakeEngine);

        Assert.False(mainWindowVm.HasOpenArchive);
        Assert.False(mainWindowVm.IsCompressDialogVisible);
        Assert.Equal("Ready", mainWindowVm.StatusText);

        // 2. Drag and drop a folder onto empty state
        var folderToCompress = Path.Combine(_tempDir, "project_source");
        Directory.CreateDirectory(folderToCompress);
        await mainWindowVm.HandleDroppedPathsAsync([folderToCompress]);

        Assert.True(mainWindowVm.IsCompressDialogVisible);
        Assert.Equal(folderToCompress, mainWindowVm.Settings.SourcePath);
        Assert.Equal(folderToCompress + ".zrus", mainWindowVm.Settings.DestinationPath);

        // 3. Preset selection (Ultra)
        mainWindowVm.Settings.SelectPreset("Ultra");
        Assert.Equal("Ultra", mainWindowVm.Settings.ActivePreset);
        Assert.Equal(22, mainWindowVm.Settings.CompressionLevel);

        // 4. Setup mock entries for after compression
        var now = DateTimeOffset.UtcNow;
        fakeEngine.EntriesToReturn =
        [
            new ArchiveEntry("src/RusZip.Core/Engine.cs", 4000, 1200, now, false),
            new("src/RusZip.Desktop/App.axaml", 2000, 800, now, false),
            new("README.md", 1000, 400, now, false)
        ];

        // 5. Execute compress
        await mainWindowVm.ExecuteCompressAsync();

        Assert.False(mainWindowVm.IsCompressDialogVisible);
        Assert.True(mainWindowVm.HasOpenArchive);
        Assert.NotNull(fakeEngine.LastCompressionRequest);
        Assert.Equal(22, fakeEngine.LastCompressionRequest.CompressionLevel);

        // 6. Verify ProDataGrid projection and aggregate totals
        var browser = mainWindowVm.Browser;
        Assert.Equal(3, browser.TotalEntries);
        Assert.Equal(7000, browser.TotalUncompressedBytes);
        Assert.Equal(2400, browser.TotalCompressedBytes);
        Assert.Equal(2, browser.RootItems.Count); // "src" folder and "README.md" file
        Assert.NotNull(browser.GridSource);
        Assert.Equal(2, browser.GridSource!.RootItems!.Cast<object>().Count());

        // 7. Select deep item and verify Breadcrumbs
        var deepItem = browser.FindItemByPath("src/RusZip.Desktop/App.axaml");
        Assert.NotNull(deepItem);
        browser.SelectedItem = deepItem;

        Assert.Equal(4, browser.Breadcrumbs.Count);
        Assert.Equal("Archive", browser.Breadcrumbs[0].Name);
        Assert.Equal("src", browser.Breadcrumbs[1].Name);
        Assert.Equal("RusZip.Desktop", browser.Breadcrumbs[2].Name);
        Assert.Equal("App.axaml", browser.Breadcrumbs[3].Name);
        Assert.True(browser.Breadcrumbs[3].IsLast);

        // 8. Breadcrumb navigation back to "src"
        browser.NavigateToBreadcrumbCommand.Execute(browser.Breadcrumbs[1]);
        Assert.Equal(2, browser.Breadcrumbs.Count);
        Assert.NotNull(browser.SelectedItem);
        Assert.Equal("src", browser.SelectedItem.Name);

        // 9. Item Extraction
        var extractDest = Path.Combine(_tempDir, "extracted_out");
        mainWindowVm.RequestExtractDestinationFolder = () => Task.FromResult<string?>(extractDest);
        await mainWindowVm.ExecuteExtractItemAsync(browser.SelectedItem, extractDest);

        Assert.NotNull(fakeEngine.LastExtractionRequest);
        Assert.Equal(extractDest, fakeEngine.LastExtractionRequest.DestinationDirectory);

        // 10. Close Archive -> Return to empty state
        mainWindowVm.CloseArchive();
        Assert.False(mainWindowVm.HasOpenArchive);
        Assert.Equal("Ready", mainWindowVm.StatusText);
    }

    [Fact]
    public void EndToEnd_ThemeAndIconSynchronization_CyclesSeamlessly()
    {
        var fakeEngine = new FakeArchiveEngine();
        var mainWindowVm = new MainWindowViewModel(fakeEngine);

        // Initial System theme
        Assert.Equal(ThemeMode.System, mainWindowVm.CurrentTheme);
        Assert.Equal("System", mainWindowVm.ThemeDisplayName);
        Assert.Equal("Icon.ThemeLight", mainWindowVm.ThemeIconKey);

        // Toggle to Dark
        mainWindowVm.ToggleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.Dark, mainWindowVm.CurrentTheme);
        Assert.True(mainWindowVm.IsDarkTheme);
        Assert.False(mainWindowVm.IsLightTheme);
        Assert.False(mainWindowVm.IsSystemTheme);
        Assert.Equal("Icon.ThemeDark", mainWindowVm.ThemeIconKey);

        // Toggle to Light
        mainWindowVm.ToggleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.Light, mainWindowVm.CurrentTheme);
        Assert.False(mainWindowVm.IsDarkTheme);
        Assert.True(mainWindowVm.IsLightTheme);
        Assert.False(mainWindowVm.IsSystemTheme);
        Assert.Equal("Icon.ThemeLight", mainWindowVm.ThemeIconKey);

        // Toggle back to System
        mainWindowVm.ToggleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.System, mainWindowVm.CurrentTheme);
        Assert.False(mainWindowVm.IsDarkTheme);
        Assert.False(mainWindowVm.IsLightTheme);
        Assert.True(mainWindowVm.IsSystemTheme);
        Assert.Equal("Icon.ThemeLight", mainWindowVm.ThemeIconKey);
    }
}

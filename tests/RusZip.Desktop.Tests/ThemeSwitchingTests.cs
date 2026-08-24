using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using RusZip.Desktop.Models;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Tests;

public class ThemeSwitchingTests
{
    private class DummyArchiveEngine : IArchiveEngine
    {
        public Task CompressAsync(ArchiveCompressionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<AppendResult> AppendAsync(ArchiveAppendRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new AppendResult(true, request.ArchivePath, "zrus", 0, 0, 0, 0, 0, 0, 0, 1.0, 0));
        public Task<ArchiveDeleteResult> DeleteEntriesAsync(ArchiveDeleteRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new ArchiveDeleteResult(true, request.ArchivePath, 0, 0, 0, 0, 0));
        public Task<ExtractionResult> ExtractAsync(ArchiveExtractionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new ExtractionResult(0, 0, 0));
        public Task<ArchiveTestResult> TestArchiveAsync(string archivePath, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new ArchiveTestResult(true, archivePath, "zrus", 0, 0, 0.0, TimeSpan.Zero, []));
        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ArchiveEntry>>([]);
    }

    [Fact]
    public void App_SetTheme_HandlesAllVariantsGracefully()
    {
        // Should not throw even if Application.Current is null (headless test environment).
        // Record.Exception makes the no-throw contract explicit rather than a bare call with no assert.
        Assert.Null(Record.Exception(() => App.SetTheme(ThemeMode.System)));
        Assert.Null(Record.Exception(() => App.SetTheme(ThemeMode.Dark)));
        Assert.Null(Record.Exception(() => App.SetTheme(ThemeMode.Light)));
    }

    [Fact]
    public void MainWindowViewModel_InitialThemeIsSystem()
    {
        var vm = new MainWindowViewModel(new DummyArchiveEngine());

        Assert.Equal(ThemeMode.System, vm.CurrentTheme);
        Assert.True(vm.IsSystemTheme);
        Assert.False(vm.IsDarkTheme);
        Assert.False(vm.IsLightTheme);
        Assert.Equal("System", vm.ThemeDisplayName);
        Assert.Equal("Icon.ThemeLight", vm.ThemeIconKey);
    }

    [Fact]
    public void MainWindowViewModel_ToggleTheme_CyclesSystemDarkLight()
    {
        var vm = new MainWindowViewModel(new DummyArchiveEngine());

        // Initial -> System
        Assert.Equal(ThemeMode.System, vm.CurrentTheme);

        // 1st Toggle -> Dark
        vm.ToggleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.Dark, vm.CurrentTheme);
        Assert.True(vm.IsDarkTheme);
        Assert.False(vm.IsLightTheme);
        Assert.False(vm.IsSystemTheme);
        Assert.Equal("Dark", vm.ThemeDisplayName);
        Assert.Equal("Icon.ThemeDark", vm.ThemeIconKey);

        // 2nd Toggle -> Light
        vm.ToggleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.Light, vm.CurrentTheme);
        Assert.True(vm.IsLightTheme);
        Assert.False(vm.IsDarkTheme);
        Assert.False(vm.IsSystemTheme);
        Assert.Equal("Light", vm.ThemeDisplayName);
        Assert.Equal("Icon.ThemeLight", vm.ThemeIconKey);

        // 3rd Toggle -> System
        vm.ToggleThemeCommand.Execute(null);
        Assert.Equal(ThemeMode.System, vm.CurrentTheme);
        Assert.True(vm.IsSystemTheme);
        Assert.False(vm.IsDarkTheme);
        Assert.False(vm.IsLightTheme);
        Assert.Equal("System", vm.ThemeDisplayName);
        Assert.Equal("Icon.ThemeLight", vm.ThemeIconKey);
    }

    [Fact]
    public void MainWindowViewModel_ThemeChanges_RaisePropertyChangedEvents()
    {
        var vm = new MainWindowViewModel(new DummyArchiveEngine());
        var notifiedProperties = new List<string>();

        vm.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
            {
                notifiedProperties.Add(e.PropertyName);
            }
        };

        vm.CurrentTheme = ThemeMode.Dark;

        Assert.Contains(nameof(MainWindowViewModel.CurrentTheme), notifiedProperties);
        Assert.Contains(nameof(MainWindowViewModel.IsDarkTheme), notifiedProperties);
        Assert.Contains(nameof(MainWindowViewModel.IsLightTheme), notifiedProperties);
        Assert.Contains(nameof(MainWindowViewModel.IsSystemTheme), notifiedProperties);
        Assert.Contains(nameof(MainWindowViewModel.ThemeIconKey), notifiedProperties);
        Assert.Contains(nameof(MainWindowViewModel.ThemeDisplayName), notifiedProperties);
    }

    [Theory]
    [InlineData(ThemeMode.System, "System", "Icon.ThemeLight", true, false, false)]
    [InlineData(ThemeMode.Dark, "Dark", "Icon.ThemeDark", false, true, false)]
    [InlineData(ThemeMode.Light, "Light", "Icon.ThemeLight", false, false, true)]
    public void MainWindowViewModel_PropertiesMatchThemeMode(
        ThemeMode mode,
        string expectedDisplayName,
        string expectedIconKey,
        bool isSystem,
        bool isDark,
        bool isLight)
    {
        var vm = new MainWindowViewModel(new DummyArchiveEngine())
        {
            CurrentTheme = mode
        };

        Assert.Equal(mode, vm.CurrentTheme);
        Assert.Equal(expectedDisplayName, vm.ThemeDisplayName);
        Assert.Equal(expectedIconKey, vm.ThemeIconKey);
        Assert.Equal(isSystem, vm.IsSystemTheme);
        Assert.Equal(isDark, vm.IsDarkTheme);
        Assert.Equal(isLight, vm.IsLightTheme);
    }
}

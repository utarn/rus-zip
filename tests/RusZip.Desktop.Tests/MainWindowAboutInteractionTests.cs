using Avalonia.Headless.XUnit;
using RusZip.Core.Engines;
using RusZip.Desktop.Models;
using RusZip.Desktop.Services;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class MainWindowAboutInteractionTests : IDisposable
{
    private readonly string _tempDirectory;

    public MainWindowAboutInteractionTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ruszip-about-interaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { /* Ignored */ }
        }
    }

    [Fact]
    public async Task MainWindowViewModel_ShowAboutCommand_CreatesAboutViewModelAndInvokesDialog()
    {
        var engine = new UnifiedArchiveEngine();
        var associationService = FileAssociationServiceFactory.CreateDefault();
        var recentService = new JsonRecentArchivesService(Path.Combine(_tempDirectory, "recent.json"));
        var vm = new MainWindowViewModel(engine, associationService, recentService);

        AboutViewModel? capturedVm = null;
        vm.RequestShowAboutDialog = (aboutVm) =>
        {
            capturedVm = aboutVm;
            return Task.CompletedTask;
        };

        await vm.ShowAboutCommand.ExecuteAsync(null);

        Assert.NotNull(capturedVm);
        Assert.Equal("RUS ZIP", capturedVm.AppName);
        Assert.Contains("About RUS ZIP opened", vm.StatusText);
    }

    [AvaloniaFact]
    public void AboutDialog_HeadlessInstantiation_SetsDataContextProperly()
    {
        var vm = new AboutViewModel();
        var dialog = new AboutDialog(vm);

        Assert.NotNull(dialog);
        Assert.Equal(vm, dialog.DataContext);
    }
}

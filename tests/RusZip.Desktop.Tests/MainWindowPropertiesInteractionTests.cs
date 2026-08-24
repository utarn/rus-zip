using Avalonia.Headless.XUnit;
using RusZip.Core.Abstractions;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using RusZip.Desktop.Models;
using RusZip.Desktop.Services;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class MainWindowPropertiesInteractionTests : IDisposable
{
    private readonly string _tempDirectory;

    public MainWindowPropertiesInteractionTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ruszip-prop-interaction-" + Guid.NewGuid().ToString("N"));
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
    public async Task MainWindowViewModel_ShowPropertiesCommand_ComputesPropertiesAndInvokesDialog()
    {
        var sampleDir = Path.Combine(_tempDirectory, "sample");
        Directory.CreateDirectory(sampleDir);
        var originalFile = Path.Combine(sampleDir, "test.txt");
        await File.WriteAllTextAsync(originalFile, "Sample content for properties dialog");

        var zrusPath = Path.Combine(_tempDirectory, "test.zrus");
        var engine = new UnifiedArchiveEngine();
        await engine.CompressAsync(new ArchiveCompressionRequest([originalFile], zrusPath, 3));

        var associationService = FileAssociationServiceFactory.CreateDefault();
        var recentService = new JsonRecentArchivesService(Path.Combine(_tempDirectory, "recent.json"));
        var vm = new MainWindowViewModel(engine, associationService, recentService);

        ArchivePropertiesViewModel? capturedVm = null;
        vm.RequestShowPropertiesDialog = (propVm) =>
        {
            capturedVm = propVm;
            return Task.CompletedTask;
        };

        await vm.OpenArchiveAsync(zrusPath);
        Assert.True(vm.HasOpenArchive);

        await vm.ShowPropertiesCommand.ExecuteAsync(null);

        Assert.NotNull(capturedVm);
        Assert.Equal(zrusPath, capturedVm.ArchivePath);
        Assert.Contains("Properties loaded", vm.StatusText);
    }

    [AvaloniaFact]
    public void ArchivePropertiesDialog_HeadlessInstantiation_SetsDataContextProperly()
    {
        var vm = new ArchivePropertiesViewModel
        {
            ArchivePath = "/tmp/test.zip",
            ContainerFormat = "Zip",
            CompressionMethod = "Deflate",
            TotalUncompressedSize = 1024,
            TotalCompressedSize = 512,
            TotalFiles = 1,
            TotalDirectories = 0
        };

        var dialog = new ArchivePropertiesDialog(vm);

        Assert.NotNull(dialog);
        Assert.Equal(vm, dialog.DataContext);
    }
}

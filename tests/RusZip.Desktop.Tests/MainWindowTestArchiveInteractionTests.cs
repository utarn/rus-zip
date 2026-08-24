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

public sealed class MainWindowTestArchiveInteractionTests : IDisposable
{
    private readonly string _tempDirectory;

    public MainWindowTestArchiveInteractionTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ruszip-test-ui-integrity-" + Guid.NewGuid().ToString("N"));
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
    public async Task MainWindowViewModel_TestArchiveCommand_RunsIntegrityCheckAndInvokesDialog()
    {
        var sampleDir = Path.Combine(_tempDirectory, "sample");
        Directory.CreateDirectory(sampleDir);
        await File.WriteAllTextAsync(Path.Combine(sampleDir, "file.txt"), "Testing integrity content");

        var zrusPath = Path.Combine(_tempDirectory, "test.zrus");
        var engine = new UnifiedArchiveEngine();
        await engine.CompressAsync(new ArchiveCompressionRequest([sampleDir], zrusPath, 3));

        var associationService = FileAssociationServiceFactory.CreateDefault();
        var recentService = new JsonRecentArchivesService(Path.Combine(_tempDirectory, "recent.json"));
        var vm = new MainWindowViewModel(engine, associationService, recentService);

        ArchiveTestResult? capturedResult = null;
        vm.RequestShowTestResultDialog = (res) =>
        {
            capturedResult = res;
            return Task.CompletedTask;
        };

        await vm.OpenArchiveAsync(zrusPath);
        Assert.True(vm.HasOpenArchive);

        await vm.TestArchiveCommand.ExecuteAsync(null);

        Assert.NotNull(capturedResult);
        Assert.True(capturedResult.IsSuccess);
        Assert.Contains("passed", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void ArchiveTestResultDialog_HeadlessInstantiation_SetsDataContextProperly()
    {
        var result = new ArchiveTestResult(
            IsSuccess: true,
            ArchivePath: "/path/to/archive.zrus",
            Format: "zrus",
            TotalEntries: 5,
            UncompressedBytes: 1024,
            ThroughputMBps: 50.0,
            Duration: TimeSpan.FromMilliseconds(20),
            Errors: []
        );

        var vm = new ArchiveTestResultViewModel(result);
        var dialog = new ArchiveTestResultDialog(vm);

        Assert.NotNull(dialog);
        Assert.Equal(vm, dialog.DataContext);
    }
}

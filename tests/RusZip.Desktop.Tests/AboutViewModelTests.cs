using RusZip.Desktop.ViewModels;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class AboutViewModelTests
{
    [Fact]
    public void AboutViewModel_PopulatesSupportedFormatsMatrix()
    {
        var vm = new AboutViewModel();

        Assert.Equal("rus-zip", vm.AppName);
        Assert.False(string.IsNullOrEmpty(vm.Version));
        Assert.NotEmpty(vm.SupportedFormats);

        var extList = vm.SupportedFormats.Select(f => f.PrimaryExtension).ToList();
        Assert.Contains(".zrus", extList);
        Assert.Contains(".zip", extList);
        Assert.Contains(".zst", extList);
        Assert.Contains(".7z", extList);
        Assert.Contains(".rar", extList);
        Assert.Contains(".tar.gz", extList);
        Assert.Contains(".gz", extList);

        var zrusFormat = vm.SupportedFormats.First(f => f.PrimaryExtension == ".zrus");
        Assert.True(zrusFormat.IsReadWrite);
        Assert.Equal("Read / Write", zrusFormat.ReadWriteCapability);
        Assert.Contains("Zstandard", zrusFormat.CompressionEngine);

        var rarFormat = vm.SupportedFormats.First(f => f.PrimaryExtension == ".rar");
        Assert.False(rarFormat.IsReadWrite);
        Assert.Equal("Read-Only", rarFormat.ReadWriteCapability);
    }

    [Fact]
    public void GenerateDiagnosticsReport_FormatsExpectedSystemInfo()
    {
        var vm = new AboutViewModel();
        var report = vm.GenerateDiagnosticsReport();

        Assert.NotNull(report);
        Assert.Contains("rus-zip System Diagnostics", report);
        Assert.Contains("OS Description:", report);
        Assert.Contains("OS Architecture:", report);
        Assert.Contains("Process Architecture:", report);
        Assert.Contains("Framework:", report);
        Assert.Contains("Processor Count:", report);
        Assert.Contains(".zrus", report);
        Assert.Contains(".zip", report);
    }

    [Fact]
    public async Task CopyDiagnosticsCommand_CopiesToClipboardServiceAndUpdatesStatus()
    {
        var vm = new AboutViewModel();
        string? copiedText = null;
        vm.CopyToClipboardService = text =>
        {
            copiedText = text;
            return Task.CompletedTask;
        };

        Assert.Equal("Copy System Diagnostics", vm.DiagnosticsStatus);

        await vm.CopyDiagnosticsCommand.ExecuteAsync(null);

        Assert.NotNull(copiedText);
        Assert.Contains("rus-zip System Diagnostics", copiedText);
        Assert.Contains("Copied", vm.DiagnosticsStatus);
    }

    [Fact]
    public void CloseCommand_FiresRequestClose()
    {
        var vm = new AboutViewModel();
        bool closed = false;
        vm.RequestClose += (_, _) => closed = true;

        vm.CloseCommand.Execute(null);

        Assert.True(closed);
    }
}

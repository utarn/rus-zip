using RusZip.Core.Abstractions;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using RusZip.Desktop.ViewModels;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class ArchivePropertiesViewModelTests : IDisposable
{
    private readonly string _tempDirectory;

    public ArchivePropertiesViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ruszip-properties-test-" + Guid.NewGuid().ToString("N"));
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
    public async Task CreateAsync_WithoutSelectedItem_CalculatesContainerProperties()
    {
        var sampleDir = Path.Combine(_tempDirectory, "sample");
        Directory.CreateDirectory(sampleDir);
        await File.WriteAllTextAsync(Path.Combine(sampleDir, "file1.txt"), "Content 1");
        await File.WriteAllTextAsync(Path.Combine(sampleDir, "file2.txt"), "Content 222");

        var zrusPath = Path.Combine(_tempDirectory, "archive.zrus");
        var engine = new UnifiedArchiveEngine();
        await engine.CompressAsync(new ArchiveCompressionRequest([sampleDir], zrusPath, 3));

        var vm = await ArchivePropertiesViewModel.CreateAsync(zrusPath, engine, selectedItem: null);

        Assert.Equal(zrusPath, vm.ArchivePath);
        Assert.Equal("archive.zrus", vm.ArchiveFileName);
        Assert.Contains("Zstandard", vm.ContainerFormat);
        Assert.Contains("Zstandard", vm.CompressionMethod);
        Assert.True(vm.TotalUncompressedSize > 0);
        Assert.True(vm.TotalCompressedSize > 0);
        Assert.True(vm.TotalFiles >= 2);
        Assert.False(vm.HasSelectedItem);
    }

    [Fact]
    public async Task CreateAsync_WithSelectedItem_CalculatesItemAndContainerProperties()
    {
        var sampleDir = Path.Combine(_tempDirectory, "sample_item");
        Directory.CreateDirectory(sampleDir);
        var originalFile = Path.Combine(sampleDir, "doc.txt");
        await File.WriteAllTextAsync(originalFile, "Sample doc for properties");

        var zrusPath = Path.Combine(_tempDirectory, "test.zrus");
        var engine = new UnifiedArchiveEngine();
        await engine.CompressAsync(new ArchiveCompressionRequest([originalFile], zrusPath, 3));

        var selectedItem = new ArchiveItemViewModel
        {
            Name = "doc.txt",
            RelativePath = "doc.txt",
            ItemType = ArchiveItemType.File,
            UncompressedSize = 24,
            CompressedSize = 18,
            LastModified = DateTimeOffset.UtcNow,
            Attributes = "-rw-r--r--"
        };

        var vm = await ArchivePropertiesViewModel.CreateAsync(zrusPath, engine, selectedItem);

        Assert.True(vm.HasSelectedItem);
        Assert.Equal("doc.txt", vm.SelectedItemName);
        Assert.Equal("doc.txt", vm.SelectedItemRelativePath);
        Assert.Equal("File", vm.SelectedItemType);
        Assert.Equal("24 B", vm.FormattedSelectedItemUncompressedSize);
        Assert.Equal("18 B", vm.FormattedSelectedItemCompressedSize);
        Assert.Equal("-rw-r--r--", vm.SelectedItemPosixMode);
    }

    [Fact]
    public void CloseCommand_FiresRequestClose()
    {
        var vm = new ArchivePropertiesViewModel();
        bool closed = false;
        vm.RequestClose += (_, _) => closed = true;

        vm.CloseCommand.Execute(null);

        Assert.True(closed);
    }
}

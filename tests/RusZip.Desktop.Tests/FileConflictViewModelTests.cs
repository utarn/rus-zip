using RusZip.Core.Abstractions;
using RusZip.Desktop.ViewModels;
using Xunit;

namespace RusZip.Desktop.Tests;

public class FileConflictViewModelTests
{
    [Fact]
    public void Constructor_FormatsMetadataCorrectly()
    {
        var targetPath = Path.Combine(Path.GetTempPath(), "output", "docs", "report.pdf");
        var existingMod = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var entryMod = new DateTimeOffset(2026, 2, 20, 14, 30, 0, TimeSpan.Zero);

        var context = new FileConflictContext(
            TargetPath: targetPath,
            RelativeEntryPath: "docs/report.pdf",
            EntryUncompressedSize: 2048,
            EntryLastModified: entryMod,
            ExistingFileSize: 1024,
            ExistingLastModified: existingMod
        );

        var vm = new FileConflictViewModel(context);

        Assert.Equal("report.pdf", vm.FileName);
        Assert.Equal(Path.GetDirectoryName(targetPath), vm.DirectoryPath);
        Assert.Equal("docs/report.pdf", vm.RelativePath);
        Assert.Contains("1", vm.ExistingFileSizeFormatted);
        Assert.Contains("2", vm.IncomingFileSizeFormatted);
        Assert.Equal(existingMod.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"), vm.ExistingLastModifiedFormatted);
        Assert.Equal(entryMod.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"), vm.IncomingLastModifiedFormatted);
    }

    [Fact]
    public void Constructor_WhenEntryLastModifiedIsNull_ShowsUnknown()
    {
        var context = new FileConflictContext(
            TargetPath: "/test/file.txt",
            RelativeEntryPath: "file.txt",
            EntryUncompressedSize: 100,
            EntryLastModified: null,
            ExistingFileSize: 200,
            ExistingLastModified: DateTimeOffset.UtcNow
        );

        var vm = new FileConflictViewModel(context);

        Assert.Equal("Unknown", vm.IncomingLastModifiedFormatted);
    }

    [Theory]
    [InlineData(nameof(FileConflictViewModel.Overwrite), FileConflictResolution.Overwrite)]
    [InlineData(nameof(FileConflictViewModel.OverwriteAll), FileConflictResolution.OverwriteAll)]
    [InlineData(nameof(FileConflictViewModel.Skip), FileConflictResolution.Skip)]
    [InlineData(nameof(FileConflictViewModel.SkipAll), FileConflictResolution.SkipAll)]
    [InlineData(nameof(FileConflictViewModel.Abort), FileConflictResolution.Abort)]
    public void Commands_InvokeCloseWithResult_WithExpectedResolution(string actionName, FileConflictResolution expected)
    {
        var context = new FileConflictContext(
            TargetPath: "/test/file.txt",
            RelativeEntryPath: "file.txt",
            EntryUncompressedSize: 100,
            EntryLastModified: null,
            ExistingFileSize: 200,
            ExistingLastModified: DateTimeOffset.UtcNow
        );

        var vm = new FileConflictViewModel(context);
        FileConflictResolution? actualResult = null;
        vm.CloseWithResult = res => actualResult = res;

        switch (actionName)
        {
            case nameof(FileConflictViewModel.Overwrite):
                vm.OverwriteCommand.Execute(null);
                break;
            case nameof(FileConflictViewModel.OverwriteAll):
                vm.OverwriteAllCommand.Execute(null);
                break;
            case nameof(FileConflictViewModel.Skip):
                vm.SkipCommand.Execute(null);
                break;
            case nameof(FileConflictViewModel.SkipAll):
                vm.SkipAllCommand.Execute(null);
                break;
            case nameof(FileConflictViewModel.Abort):
                vm.AbortCommand.Execute(null);
                break;
        }

        Assert.Equal(expected, actualResult);
    }

    [Fact]
    public void FileConflictResolution_DefaultValue_IsAbort()
    {
        Assert.Equal(FileConflictResolution.Abort, default(FileConflictResolution));
        Assert.Equal(0, (int)FileConflictResolution.Abort);
        Assert.Equal(1, (int)FileConflictResolution.Overwrite);
        Assert.Equal(2, (int)FileConflictResolution.OverwriteAll);
        Assert.Equal(3, (int)FileConflictResolution.Skip);
        Assert.Equal(4, (int)FileConflictResolution.SkipAll);
    }
}

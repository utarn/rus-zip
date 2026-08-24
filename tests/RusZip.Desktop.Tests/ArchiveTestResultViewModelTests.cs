using RusZip.Core.Models;
using RusZip.Desktop.ViewModels;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class ArchiveTestResultViewModelTests
{
    [Fact]
    public void ArchiveTestResultViewModel_SuccessfulResult_FormatsExpectedProperties()
    {
        var result = new ArchiveTestResult(
            IsSuccess: true,
            ArchivePath: "/tmp/backup.zrus",
            Format: "zrus",
            TotalEntries: 42,
            UncompressedBytes: 10485760, // 10 MB
            ThroughputMBps: 125.50,
            Duration: TimeSpan.FromMilliseconds(80),
            Errors: Array.Empty<string>()
        );

        var vm = new ArchiveTestResultViewModel(result);

        Assert.True(vm.IsSuccess);
        Assert.False(vm.HasErrors);
        Assert.Equal("backup.zrus", vm.ArchiveFileName);
        Assert.Equal("ZRUS", vm.Format);
        Assert.Equal("Archive Integrity Verified", vm.HeaderTitle);
        Assert.Contains("passed integrity verification", vm.StatusSummary);
        Assert.Equal("42 entries", vm.TotalEntriesText);
        Assert.Equal("10.0 MB", vm.UncompressedBytesText);
        Assert.Equal("80 ms", vm.DurationText);
        Assert.Equal("125.50 MB/s", vm.ThroughputText);
        Assert.Empty(vm.Errors);
    }

    [Fact]
    public void ArchiveTestResultViewModel_FailedResult_FormatsErrorsAndStatus()
    {
        var errors = new List<string> { "Checksum mismatch on entry 'data.bin'", "Zstandard frame truncated" };
        var result = new ArchiveTestResult(
            IsSuccess: false,
            ArchivePath: "/tmp/corrupt.zip",
            Format: "zip",
            TotalEntries: 10,
            UncompressedBytes: 5242880,
            ThroughputMBps: 45.2,
            Duration: TimeSpan.FromSeconds(1.5),
            Errors: errors
        );

        var vm = new ArchiveTestResultViewModel(result);

        Assert.False(vm.IsSuccess);
        Assert.True(vm.HasErrors);
        Assert.Equal("Archive Integrity Check Failed", vm.HeaderTitle);
        Assert.Contains("detected 2 error(s)", vm.StatusSummary);
        Assert.Equal(2, vm.Errors.Count);
        Assert.Equal("1.50 s", vm.DurationText);
    }

    [Fact]
    public void ArchiveTestResultViewModel_CloseCommand_FiresRequestCloseEvent()
    {
        var result = new ArchiveTestResult(
            IsSuccess: true,
            ArchivePath: "/tmp/test.zrus",
            Format: "zrus",
            TotalEntries: 1,
            UncompressedBytes: 100,
            ThroughputMBps: 10,
            Duration: TimeSpan.FromMilliseconds(10),
            Errors: []
        );

        var vm = new ArchiveTestResultViewModel(result);
        bool closed = false;
        vm.RequestClose += (_, _) => closed = true;

        vm.CloseCommand.Execute(null);

        Assert.True(closed);
    }
}

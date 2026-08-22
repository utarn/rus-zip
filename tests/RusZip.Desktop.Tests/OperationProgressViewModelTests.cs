using RusZip.Core.Models;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Tests;

public class OperationProgressViewModelTests
{
    [Fact]
    public void Constructor_InitializesWithDefaultState()
    {
        var vm = new OperationProgressViewModel();

        Assert.False(vm.IsOperationRunning);
        Assert.Equal(0, vm.ProgressPercentage);
        Assert.Equal("-", vm.SpeedFormatted);
        Assert.Equal("-", vm.EtaFormatted);
        Assert.Equal("0 B / 0 B", vm.BytesProgressFormatted);
        Assert.Equal(string.Empty, vm.CurrentFileName);
        Assert.Equal("Preparing...", vm.StatusMessage);
    }

    [Fact]
    public void CreateCancellationTokenSource_InitializesOperation()
    {
        var vm = new OperationProgressViewModel();

        var cts = vm.CreateCancellationTokenSource();

        Assert.NotNull(cts);
        Assert.False(cts.IsCancellationRequested);
        Assert.True(vm.IsOperationRunning);
        Assert.Equal(0, vm.ProgressPercentage);
        Assert.Equal("Starting...", vm.StatusMessage);
        Assert.Equal("-", vm.SpeedFormatted);
        Assert.Equal("-", vm.EtaFormatted);
    }

    [Fact]
    public void CancelCommand_CancelsCancellationTokenSource_AndUpdatesStatus()
    {
        var vm = new OperationProgressViewModel();
        var cts = vm.CreateCancellationTokenSource();

        vm.CancelCommand.Execute(null);

        Assert.True(cts.IsCancellationRequested);
        Assert.Equal("Cancelling operation...", vm.StatusMessage);
    }

    [Fact]
    public void ReportProgress_UpdatesProperties()
    {
        var vm = new OperationProgressViewModel();
        vm.CreateCancellationTokenSource();

        var report = new ProgressReport(
            ProcessedBytes: 5242880,
            TotalBytes: 10485760,
            CurrentFileName: "video.mp4",
            Percentage: 50.0,
            ProcessedFiles: 1,
            TotalFiles: 2,
            IsIndeterminate: false
        );

        vm.ReportProgress(report);

        Assert.Equal("video.mp4", vm.CurrentFileName);
        Assert.Equal(50.0, vm.ProgressPercentage);
        Assert.False(vm.IsIndeterminate);
        Assert.Equal("5 MB / 10 MB", vm.BytesProgressFormatted);
    }

    [Fact]
    public void ReportProgress_IndeterminateTotalBytes_FormatsWithEllipsis()
    {
        var vm = new OperationProgressViewModel();
        vm.CreateCancellationTokenSource();

        var report = new ProgressReport(
            ProcessedBytes: 1048576,
            TotalBytes: -1,
            CurrentFileName: "archive.tar",
            Percentage: 0,
            ProcessedFiles: 1,
            TotalFiles: 1,
            IsIndeterminate: true
        );

        vm.ReportProgress(report);

        Assert.Equal("archive.tar", vm.CurrentFileName);
        Assert.True(vm.IsIndeterminate);
        Assert.Equal("1 MB / ...", vm.BytesProgressFormatted);
    }

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(-10, "0 B/s")]
    [InlineData(1024, "1 KB/s")]
    [InlineData(10485760, "10 MB/s")]
    [InlineData(1073741824, "1 GB/s")]
    public void FormatSpeed_FormatsCorrectly(double bytesPerSec, string expected)
    {
        var formatted = OperationProgressViewModel.FormatSpeed(bytesPerSec);
        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(-5, "00:00")]
    [InlineData(45, "00:45")]
    [InlineData(90, "01:30")]
    [InlineData(3665, "01:01:05")]
    public void FormatEta_FormatsCorrectly(int totalSeconds, string expected)
    {
        var timeSpan = TimeSpan.FromSeconds(totalSeconds);
        var formatted = OperationProgressViewModel.FormatEta(timeSpan);
        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-100, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(1099511627776, "1 TB")]
    public void FormatBytes_FormatsCorrectly(long bytes, string expected)
    {
        var formatted = OperationProgressViewModel.FormatBytes(bytes);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public async Task FinishOperationAsync_CompletesOperationAndResetsState()
    {
        var vm = new OperationProgressViewModel();
        var cts = vm.CreateCancellationTokenSource();

        Assert.True(vm.IsOperationRunning);

        await vm.FinishOperationAsync(success: true, message: "Archive created successfully");

        Assert.False(vm.IsOperationRunning);
        Assert.Equal("Archive created successfully", vm.StatusMessage);
    }

    [Fact]
    public async Task FinishOperationAsync_DefaultFailureMessage()
    {
        var vm = new OperationProgressViewModel();
        vm.CreateCancellationTokenSource();

        await vm.FinishOperationAsync(success: false);

        Assert.False(vm.IsOperationRunning);
        Assert.Equal("Operation cancelled or failed.", vm.StatusMessage);
    }
}

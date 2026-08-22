using RusZip.Core.Models;
using RusZip.Desktop.ViewModels;
using Xunit;

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
        Assert.Equal("-", vm.FormattedSpeed);
        Assert.Equal("-", vm.TransferSpeed);
        Assert.Equal("-", vm.EtaFormatted);
        Assert.Equal("-", vm.FormattedEta);
        Assert.Equal("-", vm.TimeRemaining);
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
        Assert.Equal("-", vm.FormattedSpeed);
        Assert.Equal("-", vm.TransferSpeed);
        Assert.Equal("-", vm.EtaFormatted);
        Assert.Equal("-", vm.FormattedEta);
        Assert.Equal("-", vm.TimeRemaining);
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
        Assert.Equal("5.0 MB / 10.0 MB", vm.BytesProgressFormatted);
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
        Assert.Equal("1.0 MB / ...", vm.BytesProgressFormatted);
    }

    [Fact]
    public async Task ReportProgress_AfterDelay_CalculatesSpeedAndEta()
    {
        var vm = new OperationProgressViewModel();
        vm.CreateCancellationTokenSource();

        // Wait to allow stopwatch to accumulate elapsed time
        await Task.Delay(150);

        var report = new ProgressReport(
            ProcessedBytes: 10485760, // 10 MB
            TotalBytes: 52428800,     // 50 MB
            CurrentFileName: "large.bin",
            Percentage: 20.0,
            ProcessedFiles: 1,
            TotalFiles: 5,
            IsIndeterminate: false
        );

        vm.ReportProgress(report);

        Assert.NotEqual("-", vm.SpeedFormatted);
        Assert.Equal(vm.SpeedFormatted, vm.FormattedSpeed);
        Assert.Equal(vm.SpeedFormatted, vm.TransferSpeed);
        Assert.NotEqual("-", vm.EtaFormatted);
        Assert.Equal(vm.EtaFormatted, vm.FormattedEta);
        Assert.Equal(vm.EtaFormatted, vm.TimeRemaining);
    }

    [Fact]
    public async Task ReportProgress_WhenCompleted_SetsEtaToZero()
    {
        var vm = new OperationProgressViewModel();
        vm.CreateCancellationTokenSource();

        await Task.Delay(150);

        var report = new ProgressReport(
            ProcessedBytes: 10485760,
            TotalBytes: 10485760,
            CurrentFileName: "finished.bin",
            Percentage: 100.0,
            ProcessedFiles: 1,
            TotalFiles: 1,
            IsIndeterminate: false
        );

        vm.ReportProgress(report);

        Assert.Equal("00:00", vm.EtaFormatted);
        Assert.Equal("00:00", vm.FormattedEta);
        Assert.Equal("00:00", vm.TimeRemaining);
    }

    [Fact]
    public void PropertyChanged_FiresForSpeedAndEtaAliases()
    {
        var vm = new OperationProgressViewModel();
        var changedProps = new List<string>();
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) changedProps.Add(e.PropertyName);
        };

        vm.SpeedFormatted = "45.2 MB/s";
        Assert.Contains(nameof(OperationProgressViewModel.SpeedFormatted), changedProps);
        Assert.Contains(nameof(OperationProgressViewModel.FormattedSpeed), changedProps);
        Assert.Contains(nameof(OperationProgressViewModel.TransferSpeed), changedProps);

        vm.EtaFormatted = "00:12";
        Assert.Contains(nameof(OperationProgressViewModel.EtaFormatted), changedProps);
        Assert.Contains(nameof(OperationProgressViewModel.FormattedEta), changedProps);
        Assert.Contains(nameof(OperationProgressViewModel.TimeRemaining), changedProps);
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

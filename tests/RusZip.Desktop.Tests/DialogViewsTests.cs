using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RusZip.Core.Abstractions;
using RusZip.Desktop.Services;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public class DialogViewsTests
{
    [AvaloniaFact]
    public void FileAssociationPromptDialog_DefaultConstructor_Initializes()
    {
        var dialog = new FileAssociationPromptDialog();
        Assert.NotNull(dialog);
    }

    [AvaloniaFact]
    public void FileAssociationPromptDialog_DataContextChanged_HooksCloseRequested()
    {
        var service = new LinuxAssociationService();
        var vm = new FileAssociationPromptViewModel(service);
        var dialog = new FileAssociationPromptDialog
        {
            DataContext = vm
        };

        Assert.NotNull(dialog);

        // Invoking NotNowCommand triggers CloseRequested event and closes the dialog
        vm.NotNowCommand.Execute(null);
    }

    [AvaloniaFact]
    public void FileConflictDialog_DefaultConstructor_Initializes()
    {
        var dialog = new FileConflictDialog();
        Assert.NotNull(dialog);
    }

    [AvaloniaFact]
    public void FileConflictDialog_ViewModelConstructor_SetsDataContextAndCloseHandler()
    {
        var context = new FileConflictContext(
            TargetPath: "/tmp/dest/file.txt",
            RelativeEntryPath: "file.txt",
            EntryUncompressedSize: 200,
            EntryLastModified: DateTimeOffset.UtcNow,
            ExistingFileSize: 100,
            ExistingLastModified: DateTimeOffset.UtcNow.AddMinutes(-5)
        );

        var vm = new FileConflictViewModel(context);
        var dialog = new FileConflictDialog(vm);

        Assert.Same(vm, dialog.DataContext);
        Assert.NotNull(vm.CloseWithResult);

        // Invoking resolution executes CloseWithResult
        vm.OverwriteCommand.Execute(null);
    }

    [AvaloniaFact]
    public void DeleteConfirmationDialog_DefaultConstructor_Initializes()
    {
        var dialog = new DeleteConfirmationDialog();
        Assert.NotNull(dialog);
    }

    [AvaloniaFact]
    public void DeleteConfirmationDialog_ViewModelConstructor_SetsDataContext()
    {
        var vm = new DeleteConfirmationViewModel(2, ["file1.txt", "file2.txt"], "archive.zrus");
        var dialog = new DeleteConfirmationDialog(vm);

        Assert.Same(vm, dialog.DataContext);
        Assert.NotNull(vm.CloseWithResult);

        vm.ConfirmCommand.Execute(null);
    }
}

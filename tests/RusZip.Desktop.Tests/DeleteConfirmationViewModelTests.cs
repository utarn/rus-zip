using Avalonia.Headless.XUnit;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public class DeleteConfirmationViewModelTests
{
    [Fact]
    public void Constructor_SingleItem_FormatsSingularMessage()
    {
        var vm = new DeleteConfirmationViewModel(1, ["documents/notes.txt"], "archive.zrus");

        Assert.Equal(1, vm.EntryCount);
        Assert.Single(vm.EntryPaths);
        Assert.Equal("documents/notes.txt", vm.EntryPaths[0]);
        Assert.Equal("archive.zrus", vm.ArchiveName);
        Assert.Equal("Are you sure you want to permanently delete 'documents/notes.txt' from the archive?", vm.Message);
    }

    [Fact]
    public void Constructor_MultipleItems_FormatsPluralMessage()
    {
        var vm = new DeleteConfirmationViewModel(5, ["a.txt", "b.txt", "c.txt", "d.txt", "e.txt"], "archive.zip");

        Assert.Equal(5, vm.EntryCount);
        Assert.Equal(5, vm.EntryPaths.Count);
        Assert.Equal("archive.zip", vm.ArchiveName);
        Assert.Equal("Are you sure you want to permanently delete 5 selected items from the archive?", vm.Message);
    }

    [Fact]
    public void ConfirmCommand_InvokesCloseWithResultTrue()
    {
        var vm = new DeleteConfirmationViewModel(2, ["a.txt", "b.txt"]);
        bool? result = null;
        vm.CloseWithResult = r => result = r;

        vm.ConfirmCommand.Execute(null);

        Assert.True(result);
    }

    [Fact]
    public void CancelCommand_InvokesCloseWithResultFalse()
    {
        var vm = new DeleteConfirmationViewModel(2, ["a.txt", "b.txt"]);
        bool? result = null;
        vm.CloseWithResult = r => result = r;

        vm.CancelCommand.Execute(null);

        Assert.False(result);
    }

    [AvaloniaFact]
    public void DeleteConfirmationDialog_CanInstantiateAndBind()
    {
        var vm = new DeleteConfirmationViewModel(1, ["test.txt"], "test.zrus");
        var dialog = new DeleteConfirmationDialog(vm);

        Assert.NotNull(dialog);
        Assert.Same(vm, dialog.DataContext);
    }
}

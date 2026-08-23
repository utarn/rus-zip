using Avalonia.Controls;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Views;

public partial class DeleteConfirmationDialog : Window
{
    public DeleteConfirmationDialog()
    {
        InitializeComponent();
    }

    public DeleteConfirmationDialog(DeleteConfirmationViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseWithResult = result => Close(result);
    }
}

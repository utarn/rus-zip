using Avalonia.Controls;
using RusZip.Core.Abstractions;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Views;

public partial class FileConflictDialog : Window
{
    public FileConflictDialog()
    {
        InitializeComponent();
    }

    public FileConflictDialog(FileConflictViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseWithResult = resolution => Close(resolution);
    }
}

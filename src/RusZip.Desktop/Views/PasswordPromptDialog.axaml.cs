using Avalonia.Controls;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Views;

public partial class PasswordPromptDialog : Window
{
    public PasswordPromptDialog()
    {
        InitializeComponent();
    }

    public PasswordPromptDialog(PasswordPromptViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseWithResult = result => Close(result);
    }
}

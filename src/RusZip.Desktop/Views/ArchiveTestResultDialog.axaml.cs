using Avalonia.Controls;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Views;

public partial class ArchiveTestResultDialog : Window
{
    public ArchiveTestResultDialog()
    {
        InitializeComponent();
    }

    public ArchiveTestResultDialog(ArchiveTestResultViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Close();
    }
}

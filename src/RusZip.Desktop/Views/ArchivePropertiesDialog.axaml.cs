using Avalonia.Controls;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Views;

public partial class ArchivePropertiesDialog : Window
{
    public ArchivePropertiesDialog()
    {
        InitializeComponent();
    }

    public ArchivePropertiesDialog(ArchivePropertiesViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Close();
    }
}

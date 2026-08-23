using Avalonia.Headless.XUnit;
using RusZip.Desktop.Services;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public class SettingsViewTests
{
    [AvaloniaFact]
    public void SettingsView_DefaultConstructor_Initializes()
    {
        var view = new SettingsView();
        Assert.NotNull(view);
    }

    [AvaloniaFact]
    public async Task SettingsView_DataContext_BindsToSettingsViewModel()
    {
        var service = new LinuxAssociationService();
        var vm = new SettingsViewModel(service);
        await vm.LoadAssociationsAsync();
        var view = new SettingsView
        {
            DataContext = vm
        };

        Assert.Same(vm, view.DataContext);
        Assert.NotEmpty(vm.Formats);
    }
}

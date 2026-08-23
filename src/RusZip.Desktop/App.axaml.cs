using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using RusZip.Core.Abstractions;
using RusZip.Core.Engines;
using RusZip.Desktop.Models;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;

namespace RusZip.Desktop;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static void SetTheme(ThemeMode mode)
    {
        if (Current != null)
        {
            Current.RequestedThemeVariant = mode switch
            {
                ThemeMode.Dark => ThemeVariant.Dark,
                ThemeMode.Light => ThemeVariant.Light,
                _ => ThemeVariant.Default
            };
        }
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IArchiveEngine, UnifiedArchiveEngine>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<QuickExtractViewModel>();

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var quickExtractOptions = QuickExtractCommandLineParser.Parse(desktop.Args);
            if (quickExtractOptions != null)
            {
                var quickExtractVm = Services.GetRequiredService<QuickExtractViewModel>();
                quickExtractVm.Initialize(quickExtractOptions);
                desktop.MainWindow = new QuickExtractWindow
                {
                    DataContext = quickExtractVm
                };
            }
            else
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = Services.GetRequiredService<MainWindowViewModel>()
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}

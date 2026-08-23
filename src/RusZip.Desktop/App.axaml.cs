using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using RusZip.Core.Abstractions;
using RusZip.Core.Engines;
using RusZip.Desktop.Models;
using RusZip.Desktop.Services;
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
        services.AddSingleton<ISingleInstanceCoordinator, SingleInstanceCoordinator>();
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
                var mainWindowVm = Services.GetRequiredService<MainWindowViewModel>();
                var mainWindow = new MainWindow
                {
                    DataContext = mainWindowVm
                };
                desktop.MainWindow = mainWindow;

                var coordinator = Services.GetRequiredService<ISingleInstanceCoordinator>();
                coordinator.StartListening(receivedPath =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (mainWindow.WindowState == WindowState.Minimized)
                        {
                            mainWindow.WindowState = WindowState.Normal;
                        }
                        mainWindow.Activate();

                        if (!string.IsNullOrEmpty(receivedPath) && File.Exists(receivedPath))
                        {
                            _ = mainWindowVm.OpenArchiveAsync(receivedPath);
                        }
                    });
                });

                desktop.Exit += (_, _) =>
                {
                    coordinator.Dispose();
                };

                var initialPath = Program.ExtractArchiveArgument(desktop.Args);
                if (!string.IsNullOrEmpty(initialPath) && File.Exists(initialPath))
                {
                    _ = mainWindowVm.OpenArchiveAsync(initialPath);
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}

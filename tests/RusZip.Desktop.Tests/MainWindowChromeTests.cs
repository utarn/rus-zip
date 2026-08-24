using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public class MainWindowChromeTests
{
    private static string FindDesktopProjectPath()
    {
        var currentDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(currentDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RusZip.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            throw new DirectoryNotFoundException("Could not find repository root containing RusZip.slnx");
        }

        return Path.Combine(dir.FullName, "src", "RusZip.Desktop");
    }

    [Fact]
    public void MacOsTrafficLightMargin_IsConstant76()
    {
        Assert.Equal(76.0, MainWindow.MacOsTrafficLightMargin);
    }

    [Theory]
    [InlineData(true, 76.0, 0.0, 0.0, 0.0)]
    [InlineData(false, 0.0, 0.0, 0.0, 0.0)]
    public void GetPlatformTitleBarMargin_ReturnsExpectedThickness(bool isMacOS, double left, double top, double right, double bottom)
    {
        var margin = MainWindow.GetPlatformTitleBarMargin(isMacOS);
        Assert.Equal(new Thickness(left, top, right, bottom), margin);
    }

    [Fact]
    public void MainWindowAxaml_HasRequiredExtendedChromeAttributes()
    {
        var desktopPath = FindDesktopProjectPath();
        var mainWindowAxamlFile = Path.Combine(desktopPath, "Views", "MainWindow.axaml");

        Assert.True(File.Exists(mainWindowAxamlFile), $"MainWindow.axaml not found at {mainWindowAxamlFile}");

        var doc = XDocument.Load(mainWindowAxamlFile);
        var root = doc.Root;
        Assert.NotNull(root);
        Assert.Equal("Window", root.Name.LocalName);

        Assert.Equal("True", root.Attribute("ExtendClientAreaToDecorationsHint")?.Value);
        Assert.Equal("46", root.Attribute("ExtendClientAreaTitleBarHeightHint")?.Value);
        Assert.Equal("Full", root.Attribute("WindowDecorations")?.Value);
        Assert.Equal("Mica, AcrylicBlur, Blur", root.Attribute("TransparencyLevelHint")?.Value);
        Assert.Equal("Transparent", root.Attribute("Background")?.Value);
    }

    [Fact]
    public void MainWindowAxaml_HasTitleBarAreaAndPointerPressedHandler()
    {
        var desktopPath = FindDesktopProjectPath();
        var mainWindowAxamlFile = Path.Combine(desktopPath, "Views", "MainWindow.axaml");

        var doc = XDocument.Load(mainWindowAxamlFile);
        var root = doc.Root;
        Assert.NotNull(root);

        var titleBarBorder = root.Descendants()
            .FirstOrDefault(e => e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "TitleBarArea"));

        Assert.NotNull(titleBarBorder);
        Assert.Equal("OnTitleBarPointerPressed", titleBarBorder.Attribute("PointerPressed")?.Value);

        var titleBarContentGrid = root.Descendants()
            .FirstOrDefault(e => e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "TitleBarContentGrid"));

        Assert.NotNull(titleBarContentGrid);
    }

    [Fact]
    public void MainWindowAxaml_ContainsToolbarButtonsInsideTitleBar()
    {
        var desktopPath = FindDesktopProjectPath();
        var mainWindowAxamlFile = Path.Combine(desktopPath, "Views", "MainWindow.axaml");

        var doc = XDocument.Load(mainWindowAxamlFile);
        var root = doc.Root;
        Assert.NotNull(root);

        var titleBarGrid = root.Descendants()
            .FirstOrDefault(e => e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "TitleBarContentGrid"));

        Assert.NotNull(titleBarGrid);

        var buttons = titleBarGrid.Descendants()
            .Where(e => e.Name.LocalName == "Button")
            .ToList();

        // Should have New Archive, Open Archive, Extract All, Close, and Theme Switcher
        Assert.True(buttons.Count >= 5);
    }

    [AvaloniaFact]
    public void MainWindow_ApplyPlatformWindowChrome_UpdatesTitleBarContentGridMargin()
    {
        var window = new MainWindow();

        window.ApplyPlatformWindowChrome(isMacOSOverride: true);
        var contentGrid = window.FindControl<Grid>("TitleBarContentGrid");
        Assert.NotNull(contentGrid);
        Assert.Equal(new Thickness(76, 0, 0, 0), contentGrid.Margin);

        window.ApplyPlatformWindowChrome(isMacOSOverride: false);
        Assert.Equal(new Thickness(0, 0, 0, 0), contentGrid.Margin);
    }

    [AvaloniaFact]
    public void MainWindow_ApplyPlatformWindowChrome_HidesInWindowMenuRowOnMacOS_ShowsOnOtherPlatforms()
    {
        var window = new MainWindow();

        // macOS: in-window menu bar border is hidden, native menu remains configured
        window.ApplyPlatformWindowChrome(isMacOSOverride: true);
        var menuBarBorder = window.FindControl<Border>("AppMenuBarBorder");
        Assert.NotNull(menuBarBorder);
        Assert.False(menuBarBorder.IsVisible);

        var nativeMenu = NativeMenu.GetMenu(window);
        Assert.NotNull(nativeMenu);
        Assert.Equal(6, nativeMenu.Items.Count);

        // Windows/Linux: in-window menu bar border is visible
        window.ApplyPlatformWindowChrome(isMacOSOverride: false);
        Assert.True(menuBarBorder.IsVisible);
    }

    [AvaloniaFact]
    public void MainWindow_Properties_MatchExtendedChromeConfiguration()
    {
        var window = new MainWindow();

        Assert.True(window.ExtendClientAreaToDecorationsHint);
        Assert.Equal(46, window.ExtendClientAreaTitleBarHeightHint);
        Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
    }

    [AvaloniaFact]
    public void MainWindow_ResizingAndWindowState_Supported()
    {
        var window = new MainWindow();

        Assert.True(window.CanResize);
        Assert.Equal(WindowState.Normal, window.WindowState);

        // Maximize & Restore
        window.WindowState = WindowState.Maximized;
        Assert.Equal(WindowState.Maximized, window.WindowState);

        window.WindowState = WindowState.Normal;
        Assert.Equal(WindowState.Normal, window.WindowState);

        window.WindowState = WindowState.Minimized;
        Assert.Equal(WindowState.Minimized, window.WindowState);
    }

    [AvaloniaFact]
    public void ThemeResources_AllSemanticBrushes_DefinedInApplicationResources()
    {
        var app = Application.Current;
        Assert.NotNull(app);

        string[] requiredKeys =
        [
            "SolidBackgroundFillColorBase",
            "CardBackgroundFillColorDefault",
            "CardBackgroundFillColorSecondary",
            "SurfaceStrokeColorDefault",
            "LayerFillColorDefaultBrush",
            "TextFillColorPrimary",
            "TextFillColorSecondary",
            "TextFillColorTertiary",
            "SystemFillColorCriticalBackground",
            "SystemFillColorCriticalBorderBrush",
            "SystemFillColorCritical"
        ];

        foreach (var key in requiredKeys)
        {
            Assert.True(app.TryFindResource(key, out var resource), $"Expected semantic theme resource '{key}' to be defined.");
            Assert.NotNull(resource);
        }
    }
}

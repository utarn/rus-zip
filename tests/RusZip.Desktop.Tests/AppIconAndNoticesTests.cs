using System.Xml.Linq;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class AppIconAndNoticesTests
{
    private static string FindRepositoryRoot()
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

        return dir.FullName;
    }

    [Fact]
    public void AppIconAssets_MacOsIconset_ContainsAllRequiredResolutions()
    {
        var root = FindRepositoryRoot();
        var iconsetDir = Path.Combine(root, "src", "RusZip.Desktop", "Assets", "AppIcon.iconset");
        Assert.True(Directory.Exists(iconsetDir), $"AppIcon.iconset directory not found at {iconsetDir}");

        string[] requiredFiles =
        [
            "icon_16x16.png",
            "icon_16x16@2x.png",
            "icon_32x32.png",
            "icon_32x32@2x.png",
            "icon_128x128.png",
            "icon_128x128@2x.png",
            "icon_256x256.png",
            "icon_256x256@2x.png",
            "icon_512x512.png",
            "icon_512x512@2x.png"
        ];

        foreach (var file in requiredFiles)
        {
            var filePath = Path.Combine(iconsetDir, file);
            Assert.True(File.Exists(filePath), $"Expected icon file '{file}' in AppIcon.iconset");
            var fileInfo = new FileInfo(filePath);
            Assert.True(fileInfo.Length > 0, $"Icon file '{file}' is empty");
        }
    }

    [Fact]
    public void AppIconAssets_IcoAndIcns_ExistAndAreNonEmpty()
    {
        var root = FindRepositoryRoot();
        var assetsDir = Path.Combine(root, "src", "RusZip.Desktop", "Assets");

        var icoPath = Path.Combine(assetsDir, "rus-zip.ico");
        Assert.True(File.Exists(icoPath), $"rus-zip.ico not found at {icoPath}");
        Assert.True(new FileInfo(icoPath).Length > 0, "rus-zip.ico is empty");

        var icnsPath = Path.Combine(assetsDir, "rus-zip.icns");
        Assert.True(File.Exists(icnsPath), $"rus-zip.icns not found at {icnsPath}");
        Assert.True(new FileInfo(icnsPath).Length > 0, "rus-zip.icns is empty");
    }

    [Fact]
    public void DesktopCsproj_SetsApplicationIcon()
    {
        var root = FindRepositoryRoot();
        var csprojPath = Path.Combine(root, "src", "RusZip.Desktop", "RusZip.Desktop.csproj");
        Assert.True(File.Exists(csprojPath));

        var doc = XDocument.Load(csprojPath);
        var appIconElem = doc.Descendants("ApplicationIcon").FirstOrDefault();
        Assert.NotNull(appIconElem);
        Assert.Contains("rus-zip.ico", appIconElem.Value);
    }

    [Fact]
    public void PublishScript_ConfiguresMacOsBundleIcon()
    {
        var root = FindRepositoryRoot();
        var publishScriptPath = Path.Combine(root, "scripts", "publish.sh");
        Assert.True(File.Exists(publishScriptPath));

        var text = File.ReadAllText(publishScriptPath);
        Assert.Contains("CFBundleIconFile", text);
        Assert.Contains("RusZip.icns", text);
    }

    [Fact]
    public void ThirdPartyNotices_ExistsAndCreditsMaterialIcons()
    {
        var root = FindRepositoryRoot();
        var noticesPath = Path.Combine(root, "THIRD-PARTY-NOTICES.md");
        Assert.True(File.Exists(noticesPath), $"THIRD-PARTY-NOTICES.md not found at {noticesPath}");

        var content = File.ReadAllText(noticesPath);
        Assert.Contains("Material Design Icons", content);
        Assert.Contains("Apache", content);
        Assert.Contains("2.0", content);
        Assert.Contains("http", content);
    }
}

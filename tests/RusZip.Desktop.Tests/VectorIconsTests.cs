using System.Xml.Linq;

namespace RusZip.Desktop.Tests;

public class VectorIconsTests
{
    private static readonly string[] RequiredIconKeys =
    [
        "Icon.NewArchive",
        "Icon.OpenFolder",
        "Icon.Extract",
        "Icon.Close",
        "Icon.Search",
        "Icon.Clear",
        "Icon.ThemeLight",
        "Icon.ThemeDark",
        "Icon.Folder",
        "Icon.FileCode",
        "Icon.FileDoc",
        "Icon.FileImage",
        "Icon.FileArchive",
        "Icon.FileGeneric",
        "Icon.ExpandAll",
        "Icon.CollapseAll",
        "Icon.Chevron",
        "Icon.Zap",
        "Icon.Clock"
    ];

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
    public void VectorIconsAxaml_ExistsAndContainsAllRequiredIcons()
    {
        var desktopPath = FindDesktopProjectPath();
        var vectorIconsFile = Path.Combine(desktopPath, "Styles", "VectorIcons.axaml");

        Assert.True(File.Exists(vectorIconsFile), $"VectorIcons.axaml not found at {vectorIconsFile}");

        var doc = XDocument.Load(vectorIconsFile);
        var root = doc.Root;
        Assert.NotNull(root);
        Assert.Equal("ResourceDictionary", root.Name.LocalName);

        var entries = root.Elements()
            .Where(e => e.Name.LocalName == "StreamGeometry")
            .ToDictionary(
                e => e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key")?.Value ?? string.Empty,
                e => e.Value.Trim()
            );

        foreach (var key in RequiredIconKeys)
        {
            Assert.True(entries.ContainsKey(key), $"Missing required vector icon key in VectorIcons.axaml: {key}");
            var pathData = entries[key];
            Assert.False(string.IsNullOrWhiteSpace(pathData), $"Icon path for {key} is empty");
            Assert.StartsWith("M", pathData, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AppAxaml_MergesVectorIconsDictionary()
    {
        var desktopPath = FindDesktopProjectPath();
        var appAxamlFile = Path.Combine(desktopPath, "App.axaml");

        Assert.True(File.Exists(appAxamlFile), $"App.axaml not found at {appAxamlFile}");

        var doc = XDocument.Load(appAxamlFile);
        var root = doc.Root;
        Assert.NotNull(root);

        var resourceIncludes = root.Descendants()
            .Where(e => e.Name.LocalName == "ResourceInclude")
            .Select(e => e.Attribute("Source")?.Value)
            .Where(s => s != null)
            .ToList();

        Assert.Contains(resourceIncludes, s => s!.Contains("/Styles/VectorIcons.axaml"));
    }
}

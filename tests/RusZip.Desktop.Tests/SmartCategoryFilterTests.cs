using RusZip.Core.Models;
using RusZip.Desktop.ViewModels;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class SmartCategoryFilterTests
{
    private static List<ArchiveEntry> CreateSampleEntries() =>
    [
        new("src/Program.cs", 500, 200, DateTime.UtcNow, false),
        new("src/Helper.ts", 300, 150, DateTime.UtcNow, false),
        new("docs/manual.pdf", 10000, 8000, DateTime.UtcNow, false),
        new("docs/readme.txt", 1200, 600, DateTime.UtcNow, false),
        new("images/logo.png", 2048, 1800, DateTime.UtcNow, false),
        new("images/icon.svg", 1024, 800, DateTime.UtcNow, false),
        new("media/audio.mp3", 50000, 45000, DateTime.UtcNow, false),
        new("backups/old.zip", 20000, 19000, DateTime.UtcNow, false),
        new("temp/build.tmp", 400, 200, DateTime.UtcNow, false)
    ];

    [Fact]
    public void CategoryFilter_CodeCategory_FiltersOnlyCodeFiles()
    {
        var browser = new ArchiveBrowserViewModel();
        browser.LoadEntries("test.zrus", CreateSampleEntries());

        browser.SelectedCategory = FileCategory.Code;

        var flatItems = ArchiveBrowserViewModel.GetAllFlatItems(browser.RootItems).Where(i => !i.IsDirectory).ToList();
        Assert.Equal(2, flatItems.Count);
        Assert.Contains(flatItems, i => i.Name == "Program.cs");
        Assert.Contains(flatItems, i => i.Name == "Helper.ts");
    }

    [Fact]
    public void CategoryFilter_DocumentsCategory_FiltersOnlyDocFiles()
    {
        var browser = new ArchiveBrowserViewModel();
        browser.LoadEntries("test.zrus", CreateSampleEntries());

        browser.SelectedCategory = FileCategory.Documents;

        var flatItems = ArchiveBrowserViewModel.GetAllFlatItems(browser.RootItems).Where(i => !i.IsDirectory).ToList();
        Assert.Equal(2, flatItems.Count);
        Assert.Contains(flatItems, i => i.Name == "manual.pdf");
        Assert.Contains(flatItems, i => i.Name == "readme.txt");
    }

    [Fact]
    public void CategoryFilter_ImagesCategory_FiltersOnlyImageFiles()
    {
        var browser = new ArchiveBrowserViewModel();
        browser.LoadEntries("test.zrus", CreateSampleEntries());

        browser.SelectedCategory = FileCategory.Images;

        var flatItems = ArchiveBrowserViewModel.GetAllFlatItems(browser.RootItems).Where(i => !i.IsDirectory).ToList();
        Assert.Equal(2, flatItems.Count);
        Assert.Contains(flatItems, i => i.Name == "logo.png");
        Assert.Contains(flatItems, i => i.Name == "icon.svg");
    }

    [Fact]
    public void CategoryFilter_ArchivesCategory_FiltersOnlyArchiveFiles()
    {
        var browser = new ArchiveBrowserViewModel();
        browser.LoadEntries("test.zrus", CreateSampleEntries());

        browser.SelectedCategory = FileCategory.Archives;

        var flatItems = ArchiveBrowserViewModel.GetAllFlatItems(browser.RootItems).Where(i => !i.IsDirectory).ToList();
        Assert.Single(flatItems);
        Assert.Equal("old.zip", flatItems[0].Name);
    }

    [Theory]
    [InlineData("*.png", 1, "logo.png")]
    [InlineData("*.svg", 1, "icon.svg")]
    [InlineData("src/**/*.cs", 1, "Program.cs")]
    [InlineData("!*.tmp", 8, "Program.cs")]
    [InlineData("docs/*", 2, "manual.pdf")]
    [InlineData("readme", 1, "readme.txt")]
    public void WildcardFilter_MatchesExpectedEntries(string pattern, int expectedCount, string expectedSampleName)
    {
        var browser = new ArchiveBrowserViewModel();
        browser.LoadEntries("test.zrus", CreateSampleEntries());

        browser.FilterText = pattern;

        var flatItems = ArchiveBrowserViewModel.GetAllFlatItems(browser.RootItems).Where(i => !i.IsDirectory).ToList();
        Assert.Equal(expectedCount, flatItems.Count);
        Assert.Contains(flatItems, i => i.Name == expectedSampleName);
    }

    [Fact]
    public void CategoryFilter_CombinedWithWildcard_IntersectsCriteria()
    {
        var browser = new ArchiveBrowserViewModel();
        browser.LoadEntries("test.zrus", CreateSampleEntries());

        browser.SelectedCategory = FileCategory.Code;
        browser.FilterText = "*.ts";

        var flatItems = ArchiveBrowserViewModel.GetAllFlatItems(browser.RootItems).Where(i => !i.IsDirectory).ToList();
        Assert.Single(flatItems);
        Assert.Equal("Helper.ts", flatItems[0].Name);
    }
}

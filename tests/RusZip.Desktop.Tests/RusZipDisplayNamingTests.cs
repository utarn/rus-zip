using System.Xml.Linq;
using Avalonia.Headless.XUnit;
using RusZip.Desktop.Services;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class RusZipDisplayNamingTests
{
    [Fact]
    public void AppBranding_HasExpectedConstants()
    {
        Assert.Equal("RUS ZIP", AppBranding.DisplayName);
        Assert.Equal("RUS ZIP - Compression Suite", AppBranding.MainWindowTitle);
        Assert.Equal("Quick Extract - RUS ZIP", AppBranding.QuickExtractWindowTitle);
        Assert.Equal("About RUS ZIP", AppBranding.AboutDialogTitle);
        Assert.Equal("About RUS ZIP", AppBranding.AboutMenuHeader);
        Assert.Equal("_About RUS ZIP", AppBranding.AboutInWindowMenuHeader);
        Assert.Equal("Custom Feature - RUS ZIP", AppBranding.FormatDialogTitle("Custom Feature"));
    }

    [AvaloniaFact]
    public void MainWindow_TitleAndMenuEntries_MatchExpectedBranding()
    {
        var window = new MainWindow();
        Assert.Equal("RUS ZIP - Compression Suite", window.Title);
    }

    [AvaloniaFact]
    public void QuickExtractWindow_Title_EndsWithRusZip()
    {
        var window = new QuickExtractWindow();
        Assert.Equal("Quick Extract - RUS ZIP", window.Title);
        Assert.EndsWith("- RUS ZIP", window.Title);
    }

    [AvaloniaFact]
    public void Dialogs_Titles_EndWithRusZip()
    {
        var aboutDialog = new AboutDialog(new AboutViewModel());
        Assert.Equal("About RUS ZIP", aboutDialog.Title);

        var testResultDialog = new ArchiveTestResultDialog();
        Assert.Equal("Archive Integrity Test - RUS ZIP", testResultDialog.Title);
        Assert.EndsWith("- RUS ZIP", testResultDialog.Title);

        var promptDialog = new FileAssociationPromptDialog();
        Assert.Equal("Default Archive Application - RUS ZIP", promptDialog.Title);
        Assert.EndsWith("- RUS ZIP", promptDialog.Title);

        var conflictDialog = new FileConflictDialog();
        Assert.Equal("File Conflict - RUS ZIP", conflictDialog.Title);
        Assert.EndsWith("- RUS ZIP", conflictDialog.Title);

        var deleteDialog = new DeleteConfirmationDialog();
        Assert.Equal("Confirm Deletion - RUS ZIP", deleteDialog.Title);
        Assert.EndsWith("- RUS ZIP", deleteDialog.Title);

        var propertiesDialog = new ArchivePropertiesDialog();
        Assert.Equal("Archive & Entry Properties - RUS ZIP", propertiesDialog.Title);
        Assert.EndsWith("- RUS ZIP", propertiesDialog.Title);
    }

    [Fact]
    public async Task AssociationServices_FriendlyNames_ContainRusZipBranding()
    {
        // 1. Windows Association Service
        var registry = new InMemoryWindowsRegistry();
        var winService = new WindowsAssociationService(registry, @"C:\Program Files\RusZip\rus-zip.exe");
        await winService.RegisterDefaultAssociationsAsync();

        Assert.Equal("RUS ZIP", registry.GetValue("HKEY_CURRENT_USER", @"Software\RusZip\Capabilities", "ApplicationName"));
        Assert.Contains("RUS ZIP", registry.GetValue("HKEY_CURRENT_USER", @"Software\RusZip\Capabilities", "ApplicationDescription"));
        Assert.Contains("RUS ZIP", registry.GetValue("HKEY_CURRENT_USER", @"Software\Classes\RusZip.zrus") ?? "");

        // 2. Mac Association Service
        var macPlist = MacAssociationService.GenerateDocumentTypesPlist();
        Assert.Contains("<string>RUS ZIP", macPlist);

        // 3. Linux Association Service
        var linuxContent = LinuxAssociationService.GenerateDesktopFileContent("/usr/bin/rus-zip");
        Assert.Contains("Name=RUS ZIP", linuxContent);
    }
}

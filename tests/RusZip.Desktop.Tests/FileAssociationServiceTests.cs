using RusZip.Core.Models;
using RusZip.Desktop.Models;
using RusZip.Desktop.Services;
using Xunit;

namespace RusZip.Desktop.Tests;

public class FileAssociationServiceTests : IDisposable
{
    private readonly string _tempDir;

    public FileAssociationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ruszip_assoc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { /* Ignore */ }
        }
    }

    #region Windows Association Service Tests

    [Fact]
    public async Task WindowsAssociationService_RegisterDefaultAssociations_WritesExpectedRegistryKeys()
    {
        var registry = new InMemoryWindowsRegistry();
        var exePath = @"C:\Program Files\RusZip\rus-zip.exe";
        var service = new WindowsAssociationService(registry, exePath);

        await service.RegisterDefaultAssociationsAsync();

        // 1. Check Capabilities & RegisteredApplications
        Assert.Equal("RusZip", registry.GetValue("HKEY_CURRENT_USER", @"Software\RusZip\Capabilities", "ApplicationName"));
        Assert.Equal(@"Software\RusZip\Capabilities", registry.GetValue("HKEY_CURRENT_USER", @"Software\RegisteredApplications", "RusZip"));

        // 2. Check each managed format
        foreach (var ext in WindowsAssociationService.ManagedExtensions)
        {
            var progId = WindowsAssociationService.GetProgId(ext);

            // Capabilities mapping
            Assert.Equal(progId, registry.GetValue("HKEY_CURRENT_USER", @"Software\RusZip\Capabilities\FileAssociations", ext));

            // ProgID open command
            Assert.Equal($"\"{exePath}\" \"%1\"", registry.GetValue("HKEY_CURRENT_USER", $@"Software\Classes\{progId}\shell\open\command"));
            Assert.Equal($"\"{exePath}\",0", registry.GetValue("HKEY_CURRENT_USER", $@"Software\Classes\{progId}\DefaultIcon"));

            // Extension mapping
            Assert.Equal(progId, registry.GetValue("HKEY_CURRENT_USER", $@"Software\Classes\{ext}"));

            // Shell verbs: Extract here, Extract to..., Extract to subfolder
            var hereKey = $@"Software\Classes\SystemFileAssociations\{ext}\shell\RusZip.ExtractHere";
            Assert.Equal("Extract here", registry.GetValue("HKEY_CURRENT_USER", hereKey));
            Assert.Equal(exePath, registry.GetValue("HKEY_CURRENT_USER", hereKey, "Icon"));
            Assert.Equal($"\"{exePath}\" --extract-here \"%1\"", registry.GetValue("HKEY_CURRENT_USER", $@"{hereKey}\command"));

            var toKey = $@"Software\Classes\SystemFileAssociations\{ext}\shell\RusZip.ExtractTo";
            Assert.Equal("Extract to...", registry.GetValue("HKEY_CURRENT_USER", toKey));
            Assert.Equal(exePath, registry.GetValue("HKEY_CURRENT_USER", toKey, "Icon"));
            Assert.Equal($"\"{exePath}\" --extract-to \"%1\"", registry.GetValue("HKEY_CURRENT_USER", $@"{toKey}\command"));

            var dirKey = $@"Software\Classes\SystemFileAssociations\{ext}\shell\RusZip.ExtractToSubfolder";
            Assert.Equal("Extract to subfolder", registry.GetValue("HKEY_CURRENT_USER", dirKey));
            Assert.Equal(exePath, registry.GetValue("HKEY_CURRENT_USER", dirKey, "Icon"));
            Assert.Equal($"\"{exePath}\" --extract-to-dir \"%1\"", registry.GetValue("HKEY_CURRENT_USER", $@"{dirKey}\command"));
        }
    }

    [Fact]
    public async Task WindowsAssociationService_GetAssociations_DetectsAssociatedAndUnassociatedFormats()
    {
        var registry = new InMemoryWindowsRegistry();
        var exePath = @"C:\Program Files\RusZip\rus-zip.exe";
        var service = new WindowsAssociationService(registry, exePath);

        // Initially nothing associated
        var initial = await service.GetAssociationsAsync();
        Assert.All(initial, item => Assert.False(item.IsAssociated));
        Assert.False(await service.AreAllFormatsAssociatedAsync());

        // Associate only .zrus and .zip
        await service.RegisterAssociationsAsync([".zrus", ".zip"]);

        var after = await service.GetAssociationsAsync();
        var zrus = after.First(a => a.Extension == ".zrus");
        var zip = after.First(a => a.Extension == ".zip");
        var rar = after.First(a => a.Extension == ".rar");

        Assert.True(zrus.IsAssociated);
        Assert.Equal("RusZip", zrus.CurrentHandler);
        Assert.True(zip.IsAssociated);
        Assert.Equal("RusZip", zip.CurrentHandler);
        Assert.False(rar.IsAssociated);
        Assert.False(await service.AreAllFormatsAssociatedAsync());
        Assert.True(await service.IsFormatAssociatedAsync(".zrus"));
        Assert.False(await service.IsFormatAssociatedAsync(".rar"));
    }

    [Fact]
    public async Task WindowsAssociationService_GetAssociations_DetectsThirdPartyHandler()
    {
        var registry = new InMemoryWindowsRegistry();
        var exePath = @"C:\Program Files\RusZip\rus-zip.exe";
        var service = new WindowsAssociationService(registry, exePath);

        // Set .rar to WinRAR.RAR
        registry.SetValue("HKEY_CURRENT_USER", @"Software\Classes\.rar", null, "WinRAR.RAR");

        var assocs = await service.GetAssociationsAsync();
        var rar = assocs.First(a => a.Extension == ".rar");

        Assert.False(rar.IsAssociated);
        Assert.Equal("WinRAR.RAR", rar.CurrentHandler);
    }

    [Fact]
    public async Task WindowsAssociationService_RemoveAssociations_DeletesRegistryKeys()
    {
        var registry = new InMemoryWindowsRegistry();
        var exePath = @"C:\Program Files\RusZip\rus-zip.exe";
        var service = new WindowsAssociationService(registry, exePath);

        await service.RegisterDefaultAssociationsAsync();
        Assert.True(await service.AreAllFormatsAssociatedAsync());

        await service.RemoveAssociationsAsync([".zrus", ".zip"]);

        Assert.False(await service.IsFormatAssociatedAsync(".zrus"));
        Assert.False(await service.IsFormatAssociatedAsync(".zip"));
        Assert.Null(registry.GetValue("HKEY_CURRENT_USER", @"Software\Classes\.zrus"));
        Assert.False(registry.KeyExists("HKEY_CURRENT_USER", @"Software\Classes\SystemFileAssociations\.zrus\shell\RusZip.ExtractHere"));
    }

    [Fact]
    public async Task WindowsAssociationService_RemoveAssociations_AllManagedExtensions_CleansAllVerbsAndProgIds()
    {
        var registry = new InMemoryWindowsRegistry();
        var exePath = @"C:\Program Files\RusZip\rus-zip.exe";
        var service = new WindowsAssociationService(registry, exePath);

        await service.RegisterDefaultAssociationsAsync();
        Assert.True(await service.AreAllFormatsAssociatedAsync());

        await service.RemoveAssociationsAsync(WindowsAssociationService.ManagedExtensions);

        Assert.False(await service.AreAllFormatsAssociatedAsync());
        foreach (var ext in WindowsAssociationService.ManagedExtensions)
        {
            var progId = WindowsAssociationService.GetProgId(ext);
            Assert.False(await service.IsFormatAssociatedAsync(ext));
            Assert.Null(registry.GetValue("HKEY_CURRENT_USER", $@"Software\Classes\{ext}"));
            Assert.False(registry.KeyExists("HKEY_CURRENT_USER", $@"Software\Classes\{progId}"));
            Assert.False(registry.KeyExists("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{ext}\shell\RusZip.ExtractHere"));
            Assert.False(registry.KeyExists("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{ext}\shell\RusZip.ExtractTo"));
            Assert.False(registry.KeyExists("HKEY_CURRENT_USER", $@"Software\Classes\SystemFileAssociations\{ext}\shell\RusZip.ExtractToSubfolder"));
            Assert.Null(registry.GetValue("HKEY_CURRENT_USER", @"Software\RusZip\Capabilities\FileAssociations", ext));
        }
    }

    [Fact]
    public async Task WindowsAssociationService_RemoveAssociations_PreservesForeignHandlerWithoutCaching()
    {
        var registry = new InMemoryWindowsRegistry();
        var exePath = @"C:\Program Files\RusZip\rus-zip.exe";
        var service = new WindowsAssociationService(registry, exePath);

        // Register default associations
        await service.RegisterDefaultAssociationsAsync();

        // Foreign handler overrides .rar
        registry.SetValue("HKEY_CURRENT_USER", @"Software\Classes\.rar", null, "WinRAR.RAR");

        // Remove .rar association via RusZip
        await service.RemoveAssociationsAsync([".rar"]);

        // Foreign handler is preserved, not overwritten or wiped
        Assert.Equal("WinRAR.RAR", registry.GetValue("HKEY_CURRENT_USER", @"Software\Classes\.rar"));
        // RusZip verbs and ProgID are cleaned up
        Assert.False(registry.KeyExists("HKEY_CURRENT_USER", @"Software\Classes\SystemFileAssociations\.rar\shell\RusZip.ExtractHere"));
        Assert.False(registry.KeyExists("HKEY_CURRENT_USER", @"Software\Classes\RusZip.rar"));
        Assert.Null(registry.GetValue("HKEY_CURRENT_USER", @"Software\RusZip\Capabilities\FileAssociations", ".rar"));
    }

    #endregion

    #region Linux Association Service Tests

    [Fact]
    public void LinuxAssociationService_GenerateDesktopFileContent_MatchesSpecification()
    {
        var content = LinuxAssociationService.GenerateDesktopFileContent("/usr/bin/rus-zip");

        Assert.Contains("[Desktop Entry]", content);
        Assert.Contains("Type=Application", content);
        Assert.Contains("Name=RusZip", content);
        Assert.Contains("Exec=/usr/bin/rus-zip %F", content);
        Assert.Contains("Icon=rus-zip", content);
        Assert.Contains("MimeType=application/zip;application/x-tar;application/gzip;application/x-7z-compressed;application/vnd.rar;application/x-zstd-tar;", content);
        Assert.Contains("Actions=ExtractHere;ExtractTo;ExtractToSubfolder;", content);

        Assert.Contains("[Desktop Action ExtractHere]", content);
        Assert.Contains("Name=Extract here", content);
        Assert.Contains("Exec=/usr/bin/rus-zip --extract-here %f", content);

        Assert.Contains("[Desktop Action ExtractTo]", content);
        Assert.Contains("Name=Extract to...", content);
        Assert.Contains("Exec=/usr/bin/rus-zip --extract-to %f", content);

        Assert.Contains("[Desktop Action ExtractToSubfolder]", content);
        Assert.Contains("Name=Extract to subfolder", content);
        Assert.Contains("Exec=/usr/bin/rus-zip --extract-to-dir %f", content);
    }

    [Fact]
    public async Task LinuxAssociationService_RegisterDefaultAssociations_CreatesDesktopFileAndMimeAppsList()
    {
        var desktopPath = Path.Combine(_tempDir, "applications", "rus-zip.desktop");
        var mimeappsPath = Path.Combine(_tempDir, "config", "mimeapps.list");
        var executedCommands = new List<(string Command, string[] Args)>();

        var service = new LinuxAssociationService(
            desktopFilePath: desktopPath,
            mimeappsFilePath: mimeappsPath,
            executablePath: "rus-zip",
            commandRunner: (cmd, args) =>
            {
                executedCommands.Add((cmd, args));
                return Task.FromResult(0);
            });

        await service.RegisterDefaultAssociationsAsync();

        Assert.True(File.Exists(desktopPath));
        var desktopText = await File.ReadAllTextAsync(desktopPath);
        Assert.Contains("Actions=ExtractHere;ExtractTo;ExtractToSubfolder;", desktopText);

        Assert.True(File.Exists(mimeappsPath));
        var mimeText = await File.ReadAllTextAsync(mimeappsPath);
        Assert.Contains("[Default Applications]", mimeText);
        Assert.Contains("application/zip=rus-zip.desktop", mimeText);
        Assert.Contains("application/x-zstd-tar=rus-zip.desktop", mimeText);
        Assert.Contains("application/x-7z-compressed=rus-zip.desktop", mimeText);

        Assert.NotEmpty(executedCommands);
        Assert.Contains(executedCommands, c => c.Command == "xdg-mime" && c.Args.Contains("application/zip"));

        Assert.True(await service.AreAllFormatsAssociatedAsync());
        Assert.True(await service.IsFormatAssociatedAsync(".zrus"));
        Assert.True(await service.IsFormatAssociatedAsync(".zip"));
    }

    [Fact]
    public async Task LinuxAssociationService_GetAssociations_DetectsThirdPartyHandler()
    {
        var desktopPath = Path.Combine(_tempDir, "applications", "rus-zip.desktop");
        var mimeappsPath = Path.Combine(_tempDir, "config", "mimeapps.list");

        Directory.CreateDirectory(Path.GetDirectoryName(desktopPath)!);
        File.WriteAllText(desktopPath, "[Desktop Entry]\nName=RusZip\n");

        Directory.CreateDirectory(Path.GetDirectoryName(mimeappsPath)!);
        File.WriteAllText(mimeappsPath, "[Default Applications]\napplication/zip=org.gnome.FileRoller.desktop\napplication/x-zstd-tar=rus-zip.desktop\n");

        var service = new LinuxAssociationService(desktopFilePath: desktopPath, mimeappsFilePath: mimeappsPath);

        var assocs = await service.GetAssociationsAsync();
        var zip = assocs.First(a => a.Extension == ".zip");
        var zrus = assocs.First(a => a.Extension == ".zrus");

        Assert.False(zip.IsAssociated);
        Assert.Equal("org.gnome.FileRoller", zip.CurrentHandler);
        Assert.True(zrus.IsAssociated);
        Assert.Equal("RusZip", zrus.CurrentHandler);
    }

    [Fact]
    public async Task LinuxAssociationService_RemoveAssociations_UpdatesMimeAppsList()
    {
        var desktopPath = Path.Combine(_tempDir, "applications", "rus-zip.desktop");
        var mimeappsPath = Path.Combine(_tempDir, "config", "mimeapps.list");

        var service = new LinuxAssociationService(desktopFilePath: desktopPath, mimeappsFilePath: mimeappsPath);
        await service.RegisterDefaultAssociationsAsync();
        Assert.True(await service.AreAllFormatsAssociatedAsync());

        await service.RemoveAssociationsAsync([".zip"]);

        var mimeText = await File.ReadAllTextAsync(mimeappsPath);
        Assert.DoesNotContain("application/zip=rus-zip.desktop", mimeText);
        Assert.False(await service.IsFormatAssociatedAsync(".zip"));
    }

    [Fact]
    public async Task LinuxAssociationService_RemoveAssociations_PreservesOtherHandlers()
    {
        var desktopPath = Path.Combine(_tempDir, "applications", "rus-zip.desktop");
        var mimeappsPath = Path.Combine(_tempDir, "config", "mimeapps.list");

        var service = new LinuxAssociationService(desktopFilePath: desktopPath, mimeappsFilePath: mimeappsPath);
        await service.RegisterDefaultAssociationsAsync();
        Assert.True(await service.AreAllFormatsAssociatedAsync());

        // Add a third-party handler for another MIME type
        var lines = (await File.ReadAllLinesAsync(mimeappsPath)).ToList();
        lines.Add("text/plain=gedit.desktop");
        lines.Add("application/pdf=evince.desktop");
        await File.WriteAllLinesAsync(mimeappsPath, lines);

        await service.RemoveAssociationsAsync([".zip", ".7z"]);

        var mimeText = await File.ReadAllTextAsync(mimeappsPath);
        Assert.DoesNotContain("application/zip=rus-zip.desktop", mimeText);
        Assert.DoesNotContain("application/x-7z-compressed=rus-zip.desktop", mimeText);
        Assert.Contains("text/plain=gedit.desktop", mimeText);
        Assert.Contains("application/pdf=evince.desktop", mimeText);
        Assert.False(await service.IsFormatAssociatedAsync(".zip"));
        Assert.False(await service.IsFormatAssociatedAsync(".7z"));
    }

    [Fact]
    public async Task LinuxAssociationService_RemoveAssociations_AllManagedExtensions_CleansAllMimeTypes()
    {
        var desktopPath = Path.Combine(_tempDir, "applications", "rus-zip.desktop");
        var mimeappsPath = Path.Combine(_tempDir, "config", "mimeapps.list");

        var service = new LinuxAssociationService(desktopFilePath: desktopPath, mimeappsFilePath: mimeappsPath);
        await service.RegisterDefaultAssociationsAsync();
        Assert.True(await service.AreAllFormatsAssociatedAsync());

        await service.RemoveAssociationsAsync(LinuxAssociationService.ManagedExtensions);

        Assert.False(await service.AreAllFormatsAssociatedAsync());
        var mimeText = await File.ReadAllTextAsync(mimeappsPath);
        Assert.DoesNotContain("rus-zip.desktop", mimeText);
    }

    #endregion

    #region macOS Association Service Tests

    [Fact]
    public void MacAssociationService_GenerateDocumentTypesPlist_GeneratesValidXml()
    {
        var plist = MacAssociationService.GenerateDocumentTypesPlist();

        Assert.Contains("<key>CFBundleDocumentTypes</key>", plist);
        Assert.Contains("org.zstd.tar-archive", plist);
        Assert.Contains("public.zip-archive", plist);
        Assert.Contains("org.7-zip.7-zip-archive", plist);
        Assert.Contains("<key>LSHandlerRank</key>", plist);
        Assert.Contains("<string>Owner</string>", plist);
    }

    [Fact]
    public async Task MacAssociationService_RegisterAndDetect_WorksProperly()
    {
        var executedCommands = new List<(string Command, string[] Args)>();
        var service = new MacAssociationService(
            bundleIdentifier: "com.ruszip.desktop",
            commandRunner: (cmd, args) =>
            {
                executedCommands.Add((cmd, args));
                return Task.FromResult(0);
            });

        Assert.False(await service.AreAllFormatsAssociatedAsync());

        await service.RegisterDefaultAssociationsAsync();

        Assert.True(await service.AreAllFormatsAssociatedAsync());
        Assert.True(await service.IsFormatAssociatedAsync(".zrus"));
        Assert.NotEmpty(executedCommands);
        Assert.Contains(executedCommands, c => c.Command == "duti" && c.Args.Contains("org.zstd.tar-archive"));

        await service.RemoveAssociationsAsync([".zrus"]);
        Assert.False(await service.IsFormatAssociatedAsync(".zrus"));
    }

    [Fact]
    public async Task MacAssociationService_RemoveAssociations_AllManagedExtensions_ClearsAll()
    {
        var service = new MacAssociationService(bundleIdentifier: "com.ruszip.desktop");
        await service.RegisterDefaultAssociationsAsync();
        Assert.True(await service.AreAllFormatsAssociatedAsync());

        await service.RemoveAssociationsAsync(MacAssociationService.ManagedExtensions);

        Assert.False(await service.AreAllFormatsAssociatedAsync());
        foreach (var ext in MacAssociationService.ManagedExtensions)
        {
            Assert.False(await service.IsFormatAssociatedAsync(ext));
        }
    }

    #endregion

    #region Composite & Factory Tests

    [Fact]
    public async Task CompositeFileAssociationService_DelegatesCallsProperly()
    {
        var registry = new InMemoryWindowsRegistry();
        var inner = new WindowsAssociationService(registry, "rus-zip.exe");
        var composite = new CompositeFileAssociationService(inner);

        Assert.Equal(inner.SupportedExtensions, composite.SupportedExtensions);
        Assert.False(await composite.AreAllFormatsAssociatedAsync());

        await composite.RegisterDefaultAssociationsAsync();
        Assert.True(await composite.AreAllFormatsAssociatedAsync());
        Assert.True(await composite.IsFormatAssociatedAsync(".zrus"));

        await composite.RemoveAssociationsAsync([".zrus"]);
        Assert.False(await composite.IsFormatAssociatedAsync(".zrus"));
    }

    [Fact]
    public void FileAssociationServiceFactory_CreateDefault_ReturnsServiceInstance()
    {
        var service = FileAssociationServiceFactory.CreateDefault();
        Assert.NotNull(service);
        Assert.NotEmpty(service.SupportedExtensions);

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsAssociationService>(service);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.IsType<LinuxAssociationService>(service);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.IsType<MacAssociationService>(service);
        }
    }

    #endregion
}

using RusZip.Desktop.Services;
using Xunit;

namespace RusZip.Desktop.Tests;

public class WindowsRegistryTests
{
    [Fact]
    public void InMemoryWindowsRegistry_KeyExists_DetectsKeysAndSubKeys()
    {
        var reg = new InMemoryWindowsRegistry();

        Assert.False(reg.KeyExists("HKCU", "Software\\RusZip"));

        reg.SetValue("HKCU", "Software\\RusZip", "Version", "1.0");

        Assert.True(reg.KeyExists("HKCU", "Software\\RusZip"));
        Assert.True(reg.KeyExists("HKCU", "Software"));
        Assert.True(reg.KeyExists("HKCU", ""));
    }

    [Fact]
    public void InMemoryWindowsRegistry_GetValue_ReturnsSetValue_OrNull()
    {
        var reg = new InMemoryWindowsRegistry();

        reg.SetValue("HKCU", "Software\\RusZip", "Theme", "Dark");
        reg.SetValue("HKCU", "Software\\RusZip", null, "DefaultValue");

        Assert.Equal("Dark", reg.GetValue("HKCU", "Software\\RusZip", "Theme"));
        Assert.Equal("DefaultValue", reg.GetValue("HKCU", "Software\\RusZip", null));
        Assert.Equal("DefaultValue", reg.GetValue("HKCU", "Software\\RusZip", ""));
        Assert.Null(reg.GetValue("HKCU", "Software\\RusZip", "NonExistent"));
        Assert.Null(reg.GetValue("HKCU", "NonExistentKey", "Val"));
    }

    [Fact]
    public void InMemoryWindowsRegistry_DeleteValue_RemovesValueOnly()
    {
        var reg = new InMemoryWindowsRegistry();

        reg.SetValue("HKCU", "Software\\RusZip", "Theme", "Dark");
        reg.SetValue("HKCU", "Software\\RusZip", "Lang", "EN");

        reg.DeleteValue("HKCU", "Software\\RusZip", "Theme");

        Assert.Null(reg.GetValue("HKCU", "Software\\RusZip", "Theme"));
        Assert.Equal("EN", reg.GetValue("HKCU", "Software\\RusZip", "Lang"));

        // Deleting from non-existent key should not throw
        reg.DeleteValue("HKCU", "NonExistent", "Val");
    }

    [Fact]
    public void InMemoryWindowsRegistry_DeleteSubKeyTree_RemovesKeyAndAllChildren()
    {
        var reg = new InMemoryWindowsRegistry();

        reg.SetValue("HKCU", "Software\\RusZip\\Sub1", "Val1", "A");
        reg.SetValue("HKCU", "Software\\RusZip\\Sub2", "Val2", "B");
        reg.SetValue("HKCU", "Software\\Other", "Val3", "C");

        reg.DeleteSubKeyTree("HKCU", "Software\\RusZip");

        Assert.False(reg.KeyExists("HKCU", "Software\\RusZip\\Sub1"));
        Assert.False(reg.KeyExists("HKCU", "Software\\RusZip\\Sub2"));
        Assert.True(reg.KeyExists("HKCU", "Software\\Other"));
    }

    [Fact]
    public void InMemoryWindowsRegistry_GetSubKeyNames_And_GetValueNames()
    {
        var reg = new InMemoryWindowsRegistry();

        reg.SetValue("HKCU", "Software\\RusZip\\Folder1", "ValA", "1");
        reg.SetValue("HKCU", "Software\\RusZip\\Folder2\\Nested", "ValB", "2");
        reg.SetValue("HKCU", "Software\\RusZip", "DefaultSetting", "True");
        reg.SetValue("HKCU", "Software\\RusZip", "MaxThreads", "8");

        var subKeys = reg.GetSubKeyNames("HKCU", "Software\\RusZip");
        Assert.Contains("Folder1", subKeys);
        Assert.Contains("Folder2", subKeys);

        var valueNames = reg.GetValueNames("HKCU", "Software\\RusZip");
        Assert.Contains("DefaultSetting", valueNames);
        Assert.Contains("MaxThreads", valueNames);

        Assert.Empty(reg.GetValueNames("HKCU", "NonExistent"));
    }

    [Fact]
    public void SystemWindowsRegistry_OnCurrentPlatform_ExecutesGuardedMethodsSafely()
    {
        var reg = new SystemWindowsRegistry();

        // On non-Windows, these methods return defaults without throwing
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(reg.KeyExists("HKCU", "Software"));
            Assert.Null(reg.GetValue("HKCU", "Software", "Val"));
            Assert.Empty(reg.GetSubKeyNames("HKCU", "Software"));
            Assert.Empty(reg.GetValueNames("HKCU", "Software"));

            reg.SetValue("HKCU", "Software\\Test", "Val", "1");
            reg.DeleteValue("HKCU", "Software\\Test", "Val");
            reg.DeleteSubKeyTree("HKCU", "Software\\Test");
        }
    }
}

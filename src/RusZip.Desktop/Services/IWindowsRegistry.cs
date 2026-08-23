using System.Runtime.Versioning;
using Microsoft.Win32;

namespace RusZip.Desktop.Services;

/// <summary>
/// Abstraction for Windows Registry operations to allow testing on non-Windows platforms.
/// </summary>
public interface IWindowsRegistry
{
    bool KeyExists(string rootKey, string subKey);
    string? GetValue(string rootKey, string subKey, string? valueName = null);
    void SetValue(string rootKey, string subKey, string? valueName, string value);
    void DeleteSubKeyTree(string rootKey, string subKey);
    void DeleteValue(string rootKey, string subKey, string valueName);
    IReadOnlyList<string> GetSubKeyNames(string rootKey, string subKey);
    IReadOnlyList<string> GetValueNames(string rootKey, string subKey);
}

/// <summary>
/// In-memory Windows registry simulator for unit tests.
/// </summary>
public sealed class InMemoryWindowsRegistry : IWindowsRegistry
{
    private readonly Dictionary<string, Dictionary<string, string>> _store = new(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeKey(string rootKey, string subKey)
    {
        var combined = string.IsNullOrWhiteSpace(subKey) ? rootKey : $"{rootKey}\\{subKey}";
        return combined.Replace('/', '\\').TrimEnd('\\');
    }

    public bool KeyExists(string rootKey, string subKey)
    {
        var key = NormalizeKey(rootKey, subKey);
        return _store.ContainsKey(key) || _store.Keys.Any(k => k.StartsWith(key + "\\", StringComparison.OrdinalIgnoreCase));
    }

    public string? GetValue(string rootKey, string subKey, string? valueName = null)
    {
        var key = NormalizeKey(rootKey, subKey);
        var vName = valueName ?? string.Empty;
        if (_store.TryGetValue(key, out var values) && values.TryGetValue(vName, out var val))
        {
            return val;
        }
        return null;
    }

    public void SetValue(string rootKey, string subKey, string? valueName, string value)
    {
        var key = NormalizeKey(rootKey, subKey);
        var vName = valueName ?? string.Empty;
        if (!_store.TryGetValue(key, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _store[key] = values;
        }
        values[vName] = value;
    }

    public void DeleteSubKeyTree(string rootKey, string subKey)
    {
        var key = NormalizeKey(rootKey, subKey);
        var keysToRemove = _store.Keys
            .Where(k => k.Equals(key, StringComparison.OrdinalIgnoreCase) || k.StartsWith(key + "\\", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var k in keysToRemove)
        {
            _store.Remove(k);
        }
    }

    public void DeleteValue(string rootKey, string subKey, string valueName)
    {
        var key = NormalizeKey(rootKey, subKey);
        if (_store.TryGetValue(key, out var values))
        {
            values.Remove(valueName ?? string.Empty);
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(string rootKey, string subKey)
    {
        var key = NormalizeKey(rootKey, subKey);
        var prefix = key + "\\";
        var subKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var k in _store.Keys)
        {
            if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var remainder = k[prefix.Length..];
                var slashIndex = remainder.IndexOf('\\');
                var directChild = slashIndex >= 0 ? remainder[..slashIndex] : remainder;
                subKeys.Add(directChild);
            }
        }

        return subKeys.ToList();
    }

    public IReadOnlyList<string> GetValueNames(string rootKey, string subKey)
    {
        var key = NormalizeKey(rootKey, subKey);
        if (_store.TryGetValue(key, out var values))
        {
            return values.Keys.ToList();
        }
        return [];
    }
}

/// <summary>
/// Real Windows Registry implementation guarded for Windows OS execution.
/// </summary>
public sealed class SystemWindowsRegistry : IWindowsRegistry
{
    private static RegistryKey? GetRootKey(string rootKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return rootKey.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
            "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
            _ => Registry.CurrentUser
        };
    }

    public bool KeyExists(string rootKey, string subKey)
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var root = GetRootKey(rootKey);
        if (root == null) return false;
        using var key = root.OpenSubKey(subKey);
        return key != null;
    }

    public string? GetValue(string rootKey, string subKey, string? valueName = null)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var root = GetRootKey(rootKey);
        if (root == null) return null;
        using var key = root.OpenSubKey(subKey);
        return key?.GetValue(valueName ?? string.Empty)?.ToString();
    }

    public void SetValue(string rootKey, string subKey, string? valueName, string value)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var root = GetRootKey(rootKey);
        if (root == null) return;
        using var key = root.CreateSubKey(subKey);
        key?.SetValue(valueName ?? string.Empty, value);
    }

    public void DeleteSubKeyTree(string rootKey, string subKey)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var root = GetRootKey(rootKey);
        if (root == null) return;
        try
        {
            root.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // Ignore missing
        }
    }

    public void DeleteValue(string rootKey, string subKey, string valueName)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var root = GetRootKey(rootKey);
        if (root == null) return;
        using var key = root.OpenSubKey(subKey, writable: true);
        try
        {
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }
        catch
        {
            // Ignore missing
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(string rootKey, string subKey)
    {
        if (!OperatingSystem.IsWindows()) return [];
        using var root = GetRootKey(rootKey);
        if (root == null) return [];
        using var key = root.OpenSubKey(subKey);
        return key?.GetSubKeyNames() ?? [];
    }

    public IReadOnlyList<string> GetValueNames(string rootKey, string subKey)
    {
        if (!OperatingSystem.IsWindows()) return [];
        using var root = GetRootKey(rootKey);
        if (root == null) return [];
        using var key = root.OpenSubKey(subKey);
        return key?.GetValueNames() ?? [];
    }
}

namespace RusZip.Desktop.Services;

/// <summary>
/// Factory for creating platform-appropriate file association services.
/// </summary>
public static class FileAssociationServiceFactory
{
    /// <summary>
    /// Creates the appropriate <see cref="IFileAssociationService"/> instance for the current operating system.
    /// </summary>
    /// <param name="executablePath">Optional custom executable path override.</param>
    /// <returns>Platform-specific association service.</returns>
    public static IFileAssociationService CreateDefault(string? executablePath = null)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsAssociationService(executablePath: executablePath);
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxAssociationService(executablePath: executablePath);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacAssociationService(executablePath: executablePath);
        }

        return new LinuxAssociationService(executablePath: executablePath);
    }
}

using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RusZip.Core.Models;

namespace RusZip.Desktop.ViewModels;

public sealed record FormatCapabilityItemViewModel(
    string FormatName,
    string PrimaryExtension,
    string Aliases,
    string ReadWriteCapability,
    bool IsReadWrite,
    string CompressionEngine,
    string CompressionLevels
);

public sealed partial class AboutViewModel : ObservableObject
{
    public string AppName => "rus-zip";
    public string Version { get; }
    public string BuildInfo { get; }
    public string LicenseSummary => "MIT License • Copyright (c) 2026 rus-zip Contributors";
    public string Description => "A high-performance modern compression suite with atomic multi-source archiving, streaming integrity verification, and cross-platform desktop interface.";

    [ObservableProperty] private string _diagnosticsStatus = "Copy System Diagnostics";

    public ObservableCollection<FormatCapabilityItemViewModel> SupportedFormats { get; } = [];

    public Func<string, Task>? CopyToClipboardService { get; set; }

    public event EventHandler? RequestClose;

    public AboutViewModel()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(AboutViewModel).Assembly;
        var versionAttr = asm.GetName().Version;
        Version = versionAttr != null ? $"{versionAttr.Major}.{versionAttr.Minor}.{versionAttr.Build}" : "1.0.0";
        BuildInfo = $".NET {Environment.Version} ({RuntimeInformation.ProcessArchitecture})";

        PopulateFormats();
    }

    private void PopulateFormats()
    {
        SupportedFormats.Add(new FormatCapabilityItemViewModel(
            "Zstandard Tar Archive",
            ".zrus",
            ".tar.zstd, .tzstd",
            "Read / Write",
            IsReadWrite: true,
            "Zstandard + POSIX Tar",
            "1 - 22 (Default: 9)"
        ));

        SupportedFormats.Add(new FormatCapabilityItemViewModel(
            "Standard Zip Archive",
            ".zip",
            "-",
            "Read / Write",
            IsReadWrite: true,
            "Deflate / Zip64",
            "0 - 9 (Default: 6)"
        ));

        SupportedFormats.Add(new FormatCapabilityItemViewModel(
            "Single-file Zstandard",
            ".zst",
            "-",
            "Read / Write",
            IsReadWrite: true,
            "Zstandard Stream",
            "1 - 22 (Default: 9)"
        ));

        SupportedFormats.Add(new FormatCapabilityItemViewModel(
            "7-Zip Archive",
            ".7z",
            "-",
            "Read-Only",
            IsReadWrite: false,
            "LZMA / LZMA2",
            "N/A (Decompress Only)"
        ));

        SupportedFormats.Add(new FormatCapabilityItemViewModel(
            "RAR Archive",
            ".rar",
            "-",
            "Read-Only",
            IsReadWrite: false,
            "RAR4 / RAR5 Engine",
            "N/A (Decompress Only)"
        ));

        SupportedFormats.Add(new FormatCapabilityItemViewModel(
            "GZip Compressed Tar",
            ".tar.gz",
            ".tgz",
            "Read-Only",
            IsReadWrite: false,
            "GZip + Tar",
            "N/A (Decompress Only)"
        ));

        SupportedFormats.Add(new FormatCapabilityItemViewModel(
            "Single-file GZip",
            ".gz",
            "-",
            "Read-Only",
            IsReadWrite: false,
            "DEFLATE (GZip Stream)",
            "N/A (Decompress Only)"
        ));
    }

    public string GenerateDiagnosticsReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== rus-zip System Diagnostics ===");
        sb.AppendLine($"Application: {AppName} v{Version}");
        sb.AppendLine($"OS Description: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"OS Architecture: {RuntimeInformation.OSArchitecture}");
        sb.AppendLine($"Process Architecture: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($".NET Runtime Version: {Environment.Version}");
        sb.AppendLine($"Processor Count: {Environment.ProcessorCount}");
        sb.AppendLine("Supported R/W Formats: .zrus, .tar.zstd, .tzstd, .zip, .zst");
        sb.AppendLine("Supported Read-Only Formats: .7z, .rar, .tar.gz, .tgz, .gz");
        sb.AppendLine("Generated: " + DateTimeOffset.UtcNow.ToString("u"));
        return sb.ToString();
    }

    [RelayCommand]
    public async Task CopyDiagnosticsAsync()
    {
        var report = GenerateDiagnosticsReport();
        if (CopyToClipboardService != null)
        {
            await CopyToClipboardService(report);
        }
        else
        {
            await CopyToClipboardDefaultAsync(report);
        }

        DiagnosticsStatus = "Diagnostics Copied to Clipboard!";
    }

    [RelayCommand]
    public void Close()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private static async Task CopyToClipboardDefaultAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard != null)
        {
            await desktop.MainWindow.Clipboard.SetTextAsync(text);
        }
    }
}

using Avalonia.Headless.XUnit;
using RusZip.Core.Abstractions;
using RusZip.Core.Models;
using RusZip.Desktop.Services;
using RusZip.Desktop.ViewModels;
using RusZip.Desktop.Views;
using Xunit;

namespace RusZip.Desktop.Tests;

public sealed class PasswordProtectionDesktopTests : IDisposable
{
    private readonly string _tempDir;

    public PasswordProtectionDesktopTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ruszip_desktop_pwd_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { /* Ignore */ }
        }
    }

    private class EncryptedMockArchiveEngine : IArchiveEngine
    {
        public bool IsEncrypted { get; set; } = true;
        public string? LastProvidedPassword { get; private set; }

        public Task CompressAsync(ArchiveCompressionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            LastProvidedPassword = request.Password;
            return Task.CompletedTask;
        }

        public Task<AppendResult> AppendAsync(ArchiveAppendRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            LastProvidedPassword = request.Password;
            return Task.FromResult(new AppendResult(true, request.ArchivePath, "zrus", 0, 0, 0, 0, 0, 0, 0, 1.0, 0));
        }

        public Task<ArchiveDeleteResult> DeleteEntriesAsync(ArchiveDeleteRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            LastProvidedPassword = request.Password;
            return Task.FromResult(new ArchiveDeleteResult(true, request.ArchivePath, 0, 0, 0, 0, 0));
        }

        public Task<ExtractionResult> ExtractAsync(ArchiveExtractionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            LastProvidedPassword = request.Password;
            return Task.FromResult(new ExtractionResult(100, 1, 1));
        }

        public Task<ArchiveTestResult> TestArchiveAsync(string archivePath, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => TestArchiveAsync(archivePath, null, progress, ct);

        public Task<ArchiveTestResult> TestArchiveAsync(string archivePath, string? password, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            LastProvidedPassword = password;
            return Task.FromResult(new ArchiveTestResult(true, archivePath, "zrus", 1, 100, 10.0, TimeSpan.Zero, []));
        }

        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, CancellationToken ct = default)
            => ListEntriesAsync(archivePath, null, ct);

        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, string? password, CancellationToken ct = default)
        {
            LastProvidedPassword = password;
            return Task.FromResult<IReadOnlyList<ArchiveEntry>>([
                new ArchiveEntry("secret.txt", 100, 50, DateTimeOffset.UtcNow, false, IsEncrypted: true)
            ]);
        }

        public Task<bool> IsEncryptedAsync(string archivePath, CancellationToken ct = default)
        {
            return Task.FromResult(IsEncrypted);
        }

        public Task<IReadOnlyList<string>> GetVolumePartsAsync(string archivePath, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([archivePath]);
    }

    [Fact]
    public void CompressionSettingsViewModel_PasswordValidation_WorksCorrectly()
    {
        var vm = new CompressionSettingsViewModel();
        vm.StageSources([CreateDummyFile("test.txt")]);
        vm.DestinationPath = Path.Combine(_tempDir, "out.zrus");

        Assert.False(vm.IsPasswordProtected);
        Assert.True(vm.CanCompress);
        Assert.Null(vm.PasswordErrorMessage);

        // Turn on password protection
        vm.IsPasswordProtected = true;
        Assert.False(vm.CanCompress);
        Assert.Equal("Password cannot be empty.", vm.PasswordErrorMessage);

        // Enter password without confirmation
        vm.Password = "MySecret123";
        Assert.False(vm.CanCompress);
        Assert.Equal("Passwords do not match.", vm.PasswordErrorMessage);

        // Match confirmation
        vm.ConfirmPassword = "MySecret123";
        Assert.True(vm.CanCompress);
        Assert.Null(vm.PasswordErrorMessage);

        // Check request creation
        var req = vm.CreateCompressionRequest();
        Assert.Equal("MySecret123", req.Password);
    }

    [Fact]
    public async Task MainWindowViewModel_OpenEncryptedArchive_PromptsForPassword_AndShowsEncryptedBadge()
    {
        var dummyFile = CreateDummyFile("vault.zrus");
        var mockEngine = new EncryptedMockArchiveEngine { IsEncrypted = true };
        var vm = new MainWindowViewModel(mockEngine);

        bool promptInvoked = false;
        vm.RequestPasswordPrompt = archiveName =>
        {
            promptInvoked = true;
            return Task.FromResult<string?>("VaultKey999");
        };

        await vm.OpenArchiveAsync(dummyFile);

        Assert.True(promptInvoked);
        Assert.True(vm.HasOpenArchive);
        Assert.True(vm.IsCurrentArchiveEncrypted);
        Assert.Equal("VaultKey999", mockEngine.LastProvidedPassword);
        Assert.Contains("🔒 Encrypted", vm.FormatCapabilityBadge);

        // Close archive resets encrypted badge
        vm.CloseArchive();
        Assert.False(vm.IsCurrentArchiveEncrypted);
        Assert.Empty(vm.FormatCapabilityBadge);
    }

    [AvaloniaFact]
    public void PasswordPromptDialog_InstantiatesCorrectly()
    {
        var promptVm = new PasswordPromptViewModel("vault.zrus");
        var dialog = new PasswordPromptDialog(promptVm);

        Assert.NotNull(dialog);
        Assert.Equal("Password Required - RUS ZIP", dialog.Title);
        Assert.Equal(promptVm, dialog.DataContext);
    }

    private string CreateDummyFile(string filename)
    {
        var path = Path.Combine(_tempDir, filename);
        File.WriteAllText(path, "Sample Data");
        return path;
    }
}

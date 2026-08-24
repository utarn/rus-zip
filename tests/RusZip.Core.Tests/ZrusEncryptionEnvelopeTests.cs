using System.Text;
using RusZip.Core.Abstractions;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

public sealed class ZrusEncryptionEnvelopeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IArchiveEngine _engine;

    public ZrusEncryptionEnvelopeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ruszip_zenc_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _engine = new UnifiedArchiveEngine();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { /* Ignore */ }
        }
    }

    [Fact]
    public async Task Zrus_CompressWithPassword_ProducesValidZencHeader()
    {
        // Arrange
        var srcFile = Path.Combine(_tempDir, "document.txt");
        await File.WriteAllTextAsync(srcFile, "Top Secret Classified Content 2026");

        var zrusPath = Path.Combine(_tempDir, "encrypted.zrus");

        // Act
        var req = new ArchiveCompressionRequest([srcFile], zrusPath, 9, Password: "MyStrongPassword123!");
        await _engine.CompressAsync(req);

        // Assert
        Assert.True(File.Exists(zrusPath));
        var bytes = await File.ReadAllBytesAsync(zrusPath);
        Assert.True(bytes.Length >= ZrusCryptoEnvelope.HeaderSize);

        // Magic bytes "zenc"
        Assert.Equal(0x7A, bytes[0]);
        Assert.Equal(0x65, bytes[1]);
        Assert.Equal(0x6E, bytes[2]);
        Assert.Equal(0x63, bytes[3]);
        Assert.Equal(ZrusCryptoEnvelope.CurrentVersion, bytes[4]);

        Assert.True(ZrusCryptoEnvelope.IsEncryptedFile(zrusPath));
        Assert.True(await _engine.IsEncryptedAsync(zrusPath));
    }

    [Fact]
    public async Task Zrus_ExtractWithCorrectPassword_ExtractsContentAndPreservesIntegrity()
    {
        // Arrange
        var srcDir = Path.Combine(_tempDir, "src_data");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "file1.txt"), "Content of File 1");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "file2.txt"), "Content of File 2 - Long Payload " + new string('A', 10000));

        var subDir = Path.Combine(srcDir, "nested");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "nested_file.txt"), "Nested File Content");

        var zrusPath = Path.Combine(_tempDir, "vault.zrus");
        var extractDir = Path.Combine(_tempDir, "extracted");

        const string password = "VaultPassword$2026";

        // Act
        await _engine.CompressAsync(new ArchiveCompressionRequest(srcDir, zrusPath, 9, Password: password));

        var extractResult = await _engine.ExtractAsync(new ArchiveExtractionRequest(zrusPath, extractDir, Password: password));

        // Assert
        Assert.Equal(3, extractResult.FilesExtracted);
        Assert.True(File.Exists(Path.Combine(extractDir, "file1.txt")));
        Assert.Equal("Content of File 1", await File.ReadAllTextAsync(Path.Combine(extractDir, "file1.txt")));
        Assert.True(File.Exists(Path.Combine(extractDir, "nested", "nested_file.txt")));
        Assert.Equal("Nested File Content", await File.ReadAllTextAsync(Path.Combine(extractDir, "nested", "nested_file.txt")));
    }

    [Fact]
    public async Task Zrus_ExtractWithWrongPassword_FailsImmediatelyAtHeaderVerification()
    {
        // Arrange
        var srcFile = Path.Combine(_tempDir, "secret.txt");
        await File.WriteAllTextAsync(srcFile, "Super sensitive info");

        var zrusPath = Path.Combine(_tempDir, "protected.zrus");
        var extractDir = Path.Combine(_tempDir, "bad_extracted");

        await _engine.CompressAsync(new ArchiveCompressionRequest([srcFile], zrusPath, 9, Password: "CorrectPassword"));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArchiveIntegrityException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(zrusPath, extractDir, Password: "WrongPassword")));

        Assert.Contains("Invalid archive password", ex.Message);
        Assert.False(Directory.Exists(extractDir) && Directory.GetFiles(extractDir).Length > 0);
    }

    [Fact]
    public async Task Zrus_ExtractWithoutPassword_ThrowsPasswordRequired()
    {
        // Arrange
        var srcFile = Path.Combine(_tempDir, "secret.txt");
        await File.WriteAllTextAsync(srcFile, "Super sensitive info");

        var zrusPath = Path.Combine(_tempDir, "protected.zrus");
        var extractDir = Path.Combine(_tempDir, "nopass_extracted");

        await _engine.CompressAsync(new ArchiveCompressionRequest([srcFile], zrusPath, 9, Password: "Password123"));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArchiveIntegrityException>(() =>
            _engine.ExtractAsync(new ArchiveExtractionRequest(zrusPath, extractDir, Password: null)));

        Assert.Contains("Password required", ex.Message);
    }

    [Fact]
    public async Task Zip_CompressWithPassword_ProducesWinZipAesArchive_AndExtractsCorrectly()
    {
        // Arrange
        var srcFile = Path.Combine(_tempDir, "notes.txt");
        await File.WriteAllTextAsync(srcFile, "Notes inside WinZip AES-256 encrypted zip container.");

        var zipPath = Path.Combine(_tempDir, "secured.zip");
        var extractDir = Path.Combine(_tempDir, "zip_extracted");
        const string password = "ZipSecretPassword#1";

        // Act
        await _engine.CompressAsync(new ArchiveCompressionRequest([srcFile], zipPath, 6, Password: password));

        Assert.True(File.Exists(zipPath));
        Assert.True(await _engine.IsEncryptedAsync(zipPath));

        var extractResult = await _engine.ExtractAsync(new ArchiveExtractionRequest(zipPath, extractDir, Password: password));

        // Assert
        Assert.Equal(1, extractResult.FilesExtracted);
        var extractedFile = Path.Combine(extractDir, "notes.txt");
        Assert.True(File.Exists(extractedFile));
        Assert.Equal("Notes inside WinZip AES-256 encrypted zip container.", await File.ReadAllTextAsync(extractedFile));
    }

    [Fact]
    public async Task Zrus_TestArchive_WithCorrectPassword_Passes()
    {
        // Arrange
        var srcFile = Path.Combine(_tempDir, "data.txt");
        await File.WriteAllTextAsync(srcFile, "Data for test archive pass");

        var zrusPath = Path.Combine(_tempDir, "test_pass.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest([srcFile], zrusPath, 9, Password: "TestPassword"));

        // Act
        var result = await _engine.TestArchiveAsync(zrusPath, "TestPassword");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.True(result.TotalEntries >= 1);
    }

    [Fact]
    public async Task Zrus_TestArchive_WithWrongPassword_Fails()
    {
        // Arrange
        var srcFile = Path.Combine(_tempDir, "data.txt");
        await File.WriteAllTextAsync(srcFile, "Data for test archive pass");

        var zrusPath = Path.Combine(_tempDir, "test_fail.zrus");
        await _engine.CompressAsync(new ArchiveCompressionRequest([srcFile], zrusPath, 9, Password: "TestPassword"));

        // Act
        var result = await _engine.TestArchiveAsync(zrusPath, "WrongPassword");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
    }
}

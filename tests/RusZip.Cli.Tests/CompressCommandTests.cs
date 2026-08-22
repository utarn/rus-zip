using RusZip.Cli.Commands;
using RusZip.Cli.Models;
using RusZip.Core.Abstractions;
using RusZip.Core.Engines;
using RusZip.Core.Models;
using Spectre.Console.Cli;
using Xunit;

namespace RusZip.Cli.Tests;

[Collection("CliTests")]
public sealed class CompressCommandTests : CliTestBase
{
    [Fact]
    public async Task Compress_SingleFileToZrus_JsonMode_ReturnsExitCode0_AndValidJson()
    {
        // Arrange
        var sourceFile = CreateTempFile("sample.txt", "Hello rus-zip compress test");
        var destArchive = Path.Combine(TempDirectory, "output.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(destArchive));

        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(sourceFile), result.SourcePath);
        Assert.Equal(Path.GetFullPath(destArchive), result.ArchivePath);
        Assert.Equal("zrus", result.Format);
        Assert.Equal(1, result.TotalFiles);
        Assert.True(result.UncompressedBytes > 0);
        Assert.True(result.CompressedBytes > 0);
        Assert.True(result.CompressionRatio > 0);
        Assert.True(result.ElapsedMilliseconds >= 0);
    }

    [Fact]
    public async Task Compress_DirectoryToZrus_JsonMode_ReturnsExitCode0()
    {
        // Arrange
        var sourceDir = CreateTempDirectory("sample_dir", fileCount: 4);
        var destArchive = Path.Combine(TempDirectory, "dir_output.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceDir, destArchive, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(destArchive));

        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(sourceDir), result.SourcePath);
        Assert.Equal(4, result.TotalFiles);
    }

    [Fact]
    public async Task Compress_ToZip_JsonMode_ReturnsExitCode0()
    {
        // Arrange
        var sourceFile = CreateTempFile("zip_input.txt", "Data to compress in zip format");
        var destArchive = Path.Combine(TempDirectory, "archive.zip");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(destArchive));

        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal("zip", result.Format);
        Assert.Equal(1, result.TotalFiles);
    }

    [Theory]
    [InlineData("fast")]
    [InlineData("balanced")]
    [InlineData("high")]
    [InlineData("ultra")]
    public async Task Compress_WithNamedProfiles_ReturnsExitCode0(string profile)
    {
        // Arrange
        var sourceFile = CreateTempFile($"file_{profile}.txt", $"Data for profile {profile}");
        var destArchive = Path.Combine(TempDirectory, $"archive_{profile}.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "-p", profile, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(destArchive));

        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(22)]
    public async Task Compress_WithExplicitLevels_ReturnsExitCode0(int level)
    {
        // Arrange
        var sourceFile = CreateTempFile($"level_{level}.txt", $"Data for level {level}");
        var destArchive = Path.Combine(TempDirectory, $"archive_lvl_{level}.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "-l", level.ToString(), "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(destArchive));

        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Compress_DefaultDestination_CreatesZrusFile()
    {
        // Arrange
        var sourceFile = CreateTempFile("default_dest.txt", "Testing default destination naming");
        var expectedDest = sourceFile + ".zrus";

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(expectedDest));

        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(expectedDest), result.ArchivePath);
    }

    [Fact]
    public async Task Compress_HumanMode_ReturnsExitCode0_AndDisplaysSummary()
    {
        // Arrange
        var sourceFile = CreateTempFile("human.txt", "Human readable CLI output test");
        var destArchive = Path.Combine(TempDirectory, "human.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(destArchive));
        Assert.Contains("Compression Summary", stdout);
        Assert.Contains("Archive Path", stdout);
        Assert.Contains("Total Files", stdout);
    }

    [Fact]
    public async Task Compress_NonExistentSource_ReturnsExitCode2_AndSourceNotFoundJson()
    {
        // Arrange
        var nonExistent = Path.Combine(TempDirectory, "missing_file_xyz.txt");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", nonExistent, "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("SOURCE_NOT_FOUND", err.Error.Code);
        Assert.Contains(nonExistent, err.Error.Message);
    }

    [Fact]
    public async Task Compress_InvalidProfile_ReturnsExitCode2_AndArgumentErrorJson()
    {
        // Arrange
        var sourceFile = CreateTempFile("valid.txt", "Valid file");
        var destArchive = Path.Combine(TempDirectory, "out.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "-p", "invalid_profile", "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
        Assert.Contains("Invalid compression profile", err.Error.Message);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(100)]
    public async Task Compress_InvalidLevel_ReturnsExitCode2_AndArgumentErrorJson(int invalidLevel)
    {
        // Arrange
        var sourceFile = CreateTempFile("valid2.txt", "Valid file");
        var destArchive = Path.Combine(TempDirectory, "out2.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "-l", invalidLevel.ToString(), "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
        Assert.Contains("Compression level", err.Error.Message);
        Assert.Contains(".zrus", err.Error.Message);
        Assert.Contains("1-22", err.Error.Message);
    }

    [Fact]
    public async Task Compress_ZipLevel15_ReturnsExitCode2_AndArgumentErrorJson()
    {
        // F-16: `-l 15 x.zip` must be rejected for the .zip format (valid 0-9), not silently capped.
        var sourceFile = CreateTempFile("zip_lvl15.txt", "Valid file");
        var destArchive = Path.Combine(TempDirectory, "lvl15.zip");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "-l", "15", "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
        Assert.Contains("Compression level 15 is not valid for .zip archives", err.Error.Message);
        Assert.Contains("Valid range: 0-9", err.Error.Message);
    }

    [Fact]
    public async Task Compress_ZipLevel0_ProducesStoreArchive_AndRoundTrips()
    {
        // F-16: `-l 0 x.zip` must produce a Store (no compression) archive and round-trip.
        var sourceFile = CreateTempFile("store_input.txt", "Store me without compression");
        var destArchive = Path.Combine(TempDirectory, "store_out.zip");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "-l", "0", "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(destArchive));

        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal("zip", result.Format);

        // The entry must be stored (method 0): compressed length equals uncompressed length.
        using var zip = System.IO.Compression.ZipFile.OpenRead(destArchive);
        var entry = Assert.Single(zip.Entries);
        Assert.Equal(entry.Length, entry.CompressedLength);

        // Round-trip: extract and verify content.
        var outDir = Path.Combine(TempDirectory, "store_roundtrip");
        var (extractCode, extractStdout) = await RunCliAsync("extract", destArchive, "-o", outDir, "--json");
        Assert.Equal(0, extractCode);
        var extractResult = ParseJson<ExtractResult>(extractStdout);
        Assert.True(extractResult.Success);
        Assert.Equal(1, extractResult.ExtractedFiles);
        Assert.Equal("Store me without compression", File.ReadAllText(Path.Combine(outDir, "store_input.txt")));
    }

    [Theory]
    [InlineData("output.rar")]
    [InlineData("output.7z")]
    [InlineData("output.unsupported")]
    public async Task Compress_UnsupportedCreationFormat_ReturnsExitCode2_AndUnsupportedFormatJson(string destName)
    {
        // Arrange
        var sourceFile = CreateTempFile("valid3.txt", "Valid file");
        var destArchive = Path.Combine(TempDirectory, destName);

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceFile, destArchive, "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("UNSUPPORTED_FORMAT", err.Error.Code);
    }

    [Fact]
    public async Task Compress_MissingRequiredArguments_ReturnsExitCode2_AndArgumentErrorJson()
    {
        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", "--json");

        // Assert
        Assert.Equal(2, exitCode);
        var err = ParseJson<ErrorResult>(stdout);
        Assert.False(err.Success);
        Assert.Equal("ARGUMENT_ERROR", err.Error.Code);
    }

    [Fact]
    public async Task Compress_EmptyDirectoryToZrus_ReturnsExitCode0()
    {
        // F-11: an empty directory must compress to a valid, readable .zrus archive (not a 13-byte
        // empty frame). exit 0 with 0 files, parity with the .zip path.
        var sourceDir = Path.Combine(TempDirectory, "empty_dir");
        Directory.CreateDirectory(sourceDir);
        var destArchive = Path.Combine(TempDirectory, "empty_dir.zrus");

        // Act
        var (exitCode, stdout) = await RunCliAsync("compress", sourceDir, destArchive, "--json");

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(destArchive));

        var result = ParseJson<CompressResult>(stdout);
        Assert.True(result.Success);
        Assert.Equal("zrus", result.Format);
        Assert.Equal(0, result.TotalFiles);
        Assert.Equal(0, result.UncompressedBytes);
        Assert.True(result.CompressedBytes > 0);
    }

    [Theory]
    [InlineData("fast", 3)]
    [InlineData("balanced", 9)]
    [InlineData("high", 15)]
    [InlineData("ultra", 22)]
    public async Task Compress_Profile_ResolveLevelReachesEngine(string profile, int expectedLevel)
    {
        // F-25: the e2e profile test only used `expectedLevel` in a filename, so a ResolveLevel
        // regression (e.g. always returning the default 9) would go unnoticed. This test drives
        // CompressCommand directly with a spy engine that records the CompressionLevel it receives,
        // locking the profile→level contract: fast=3, balanced=9, high=15, ultra=22.
        var src = CreateTempFile($"file_{profile}.txt", $"Payload for profile {profile} (level {expectedLevel})");
        var archive = Path.Combine(TempDirectory, $"archive_{profile}_engine_level.zrus");

        var engine = new RecordingArchiveEngine();
        var command = new CompressCommand(engine);
        var settings = new CompressSettings
        {
            SourcePath = src,
            DestinationPath = archive,
            Profile = profile,
            Json = true
        };
        var context = new CommandContext(Array.Empty<string>(), new EmptyRemainingArguments(), "compress", null);

        var exitCode = await command.ExecuteAsync(context, settings);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(archive));
        var receivedLevel = Assert.Single(engine.CompressionLevels);
        Assert.Equal(expectedLevel, receivedLevel);
    }

    /// <summary>
    /// Delegates to the real engine but records the <see cref="ArchiveCompressionRequest.CompressionLevel"/>
    /// it was asked to apply. Used to lock the profile→level mapping end-to-end through CompressCommand.
    /// </summary>
    private sealed class RecordingArchiveEngine : IArchiveEngine
    {
        private readonly IArchiveEngine _inner = new UnifiedArchiveEngine();

        public List<int> CompressionLevels { get; } = [];

        public Task CompressAsync(ArchiveCompressionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
        {
            CompressionLevels.Add(request.CompressionLevel);
            return _inner.CompressAsync(request, progress, ct);
        }

        public Task<ExtractionResult> ExtractAsync(ArchiveExtractionRequest request, IProgress<ProgressReport>? progress = null, CancellationToken ct = default)
            => _inner.ExtractAsync(request, progress, ct);

        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, CancellationToken ct = default)
            => _inner.ListEntriesAsync(archivePath, ct);
    }

    private sealed class EmptyRemainingArguments : IRemainingArguments
    {
        public ILookup<string, string?> Parsed => Enumerable.Empty<string?>().ToLookup(x => x ?? string.Empty);
        public IReadOnlyList<string> Raw => Array.Empty<string>();
    }
}

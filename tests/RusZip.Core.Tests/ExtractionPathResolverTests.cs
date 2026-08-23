using RusZip.Core.Utils;
using Xunit;

namespace RusZip.Core.Tests;

public class ExtractionPathResolverTests : IDisposable
{
    private readonly string _tempDirectory;

    public ExtractionPathResolverTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ruszip_test_extraction_path_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, recursive: true); } catch { /* ignore */ }
        }
    }

    [Theory]
    // Standard supported formats
    [InlineData("archive.zrus", "archive")]
    [InlineData("archive.tar.zstd", "archive")]
    [InlineData("archive.tzstd", "archive")]
    [InlineData("archive.zst", "archive")]
    [InlineData("archive.zip", "archive")]
    [InlineData("archive.tar.gz", "archive")]
    [InlineData("archive.tgz", "archive")]
    [InlineData("archive.7z", "archive")]
    [InlineData("archive.rar", "archive")]
    [InlineData("archive.gz", "archive")]
    // Dots within filenames
    [InlineData("release-v1.0.0.tar.gz", "release-v1.0.0")]
    [InlineData("release-v1.0.0.tgz", "release-v1.0.0")]
    [InlineData("release-v1.0.0.tar.zstd", "release-v1.0.0")]
    [InlineData("release-v1.0.0.tzstd", "release-v1.0.0")]
    [InlineData("release-v1.0.0.zst", "release-v1.0.0")]
    [InlineData("release-v1.0.0.zrus", "release-v1.0.0")]
    [InlineData("release-v1.0.0.zip", "release-v1.0.0")]
    [InlineData("release-v1.0.0.7z", "release-v1.0.0")]
    [InlineData("release-v1.0.0.rar", "release-v1.0.0")]
    [InlineData("release-v1.0.0.gz", "release-v1.0.0")]
    [InlineData("archive.test.backup.2026.08.tar.gz", "archive.test.backup.2026.08")]
    [InlineData("archive.test.backup.2026.08.tar.zstd", "archive.test.backup.2026.08")]
    [InlineData("archive.test.backup.2026.08.zst", "archive.test.backup.2026.08")]
    [InlineData("my.data.file.v2.5.1.zip", "my.data.file.v2.5.1")]
    [InlineData("dots.in.name.zrus", "dots.in.name")]
    // Case-insensitivity
    [InlineData("ARCHIVE.TAR.GZ", "ARCHIVE")]
    [InlineData("ARCHIVE.TAR.ZSTD", "ARCHIVE")]
    [InlineData("ARCHIVE.ZST", "ARCHIVE")]
    [InlineData("release.TAR.GZ", "release")]
    [InlineData("release.TAR.ZSTD", "release")]
    [InlineData("release.ZST", "release")]
    [InlineData("bundle.TGZ", "bundle")]
    [InlineData("bundle.TZSTD", "bundle")]
    [InlineData("backup.ZRUS", "backup")]
    [InlineData("photo.ZIP", "photo")]
    [InlineData("data.7Z", "data")]
    [InlineData("legacy.RAR", "legacy")]
    [InlineData("stream.GZ", "stream")]
    [InlineData("MixedCase.Tar.Gz", "MixedCase")]
    [InlineData("MixedCase.Tar.Zstd", "MixedCase")]
    [InlineData("MixedCase.Zst", "MixedCase")]
    // Paths (Unix and Windows style)
    [InlineData("/path/to/archive.tar.gz", "archive")]
    [InlineData("/path/to/archive.tar.zstd", "archive")]
    [InlineData("/path/to/archive.tzstd", "archive")]
    [InlineData("/path/to/archive.zst", "archive")]
    [InlineData("/var/tmp/downloads/release-v1.0.0.tar.gz", "release-v1.0.0")]
    [InlineData("/var/tmp/downloads/release-v1.0.0.tar.zstd", "release-v1.0.0")]
    [InlineData("/var/tmp/downloads/release-v1.0.0.zst", "release-v1.0.0")]
    [InlineData(@"C:\Users\Test\Downloads\release-v1.0.0.tar.gz", "release-v1.0.0")]
    [InlineData(@"C:\Users\Test\Downloads\release-v1.0.0.tar.zstd", "release-v1.0.0")]
    [InlineData(@"C:\Users\Test\Downloads\release-v1.0.0.zst", "release-v1.0.0")]
    [InlineData(@"C:\Users\Test\Downloads\backup.zrus", "backup")]
    [InlineData("relative/sub/folder/file.zip", "file")]
    [InlineData("/path/to/archive.tar.gz/", "archive")]
    [InlineData("/path/to/archive.tar.zstd/", "archive")]
    [InlineData("/path/to/archive.zst/", "archive")]
    // Non-archive fallback
    [InlineData("document.txt", "document")]
    [InlineData("presentation.pdf", "presentation")]
    [InlineData("unknown_file", "unknown_file")]
    [InlineData("multi.dot.custom.ext", "multi.dot.custom")]
    // Edge cases
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(".tar.gz", "")]
    [InlineData(".tar.zstd", "")]
    [InlineData(".tzstd", "")]
    [InlineData(".zst", "")]
    [InlineData(".zip", "")]
    public void GetArchiveBaseName_StripsCorrectExtension(string archivePath, string expectedBaseName)
    {
        var result = ExtractionPathResolver.GetArchiveBaseName(archivePath);
        Assert.Equal(expectedBaseName, result);
    }

    [Fact]
    public void GetArchiveBaseName_NullPath_ReturnsEmptyString()
    {
        var result = ExtractionPathResolver.GetArchiveBaseName(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ResolveUniqueDestinationDirectory_WhenDirectoryDoesNotExist_ReturnsPrimaryPath()
    {
        var baseName = "extracted_files";
        var expected = Path.Combine(_tempDirectory, baseName);

        var result = ExtractionPathResolver.ResolveUniqueDestinationDirectory(_tempDirectory, baseName);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveUniqueDestinationDirectory_WhenPrimaryDirectoryExists_ReturnsSuffix2()
    {
        var baseName = "target";
        Directory.CreateDirectory(Path.Combine(_tempDirectory, baseName));

        var expected = Path.Combine(_tempDirectory, $"{baseName}_2");
        var result = ExtractionPathResolver.ResolveUniqueDestinationDirectory(_tempDirectory, baseName);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveUniqueDestinationDirectory_WhenMultipleCollisionsExist_ReturnsNextAvailableSuffix()
    {
        var baseName = "release-v1.0.0";
        Directory.CreateDirectory(Path.Combine(_tempDirectory, baseName));
        Directory.CreateDirectory(Path.Combine(_tempDirectory, $"{baseName}_2"));
        Directory.CreateDirectory(Path.Combine(_tempDirectory, $"{baseName}_3"));
        Directory.CreateDirectory(Path.Combine(_tempDirectory, $"{baseName}_4"));

        var expected = Path.Combine(_tempDirectory, $"{baseName}_5");
        var result = ExtractionPathResolver.ResolveUniqueDestinationDirectory(_tempDirectory, baseName);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveUniqueDestinationDirectory_WhenFileCollisionExists_ReturnsSuffixedDirectory()
    {
        var baseName = "output";
        // Create a file with the primary name
        File.WriteAllText(Path.Combine(_tempDirectory, baseName), "existing file content");

        var expected = Path.Combine(_tempDirectory, $"{baseName}_2");
        var result = ExtractionPathResolver.ResolveUniqueDestinationDirectory(_tempDirectory, baseName);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveUniqueDestinationDirectory_WhenMixedDirectoryAndFileCollisionsExist_ProbesUntilFree()
    {
        var baseName = "output";
        Directory.CreateDirectory(Path.Combine(_tempDirectory, baseName));
        File.WriteAllText(Path.Combine(_tempDirectory, $"{baseName}_2"), "existing file");

        var expected = Path.Combine(_tempDirectory, $"{baseName}_3");
        var result = ExtractionPathResolver.ResolveUniqueDestinationDirectory(_tempDirectory, baseName);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveUniqueDestinationDirectory_NullArguments_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ExtractionPathResolver.ResolveUniqueDestinationDirectory(null!, "basename"));
        Assert.Throws<ArgumentNullException>(() =>
            ExtractionPathResolver.ResolveUniqueDestinationDirectory(_tempDirectory, null!));
    }
}

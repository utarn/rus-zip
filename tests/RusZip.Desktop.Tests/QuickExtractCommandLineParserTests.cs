using RusZip.Desktop.Models;
using Xunit;

namespace RusZip.Desktop.Tests;

public class QuickExtractCommandLineParserTests
{
    [Theory]
    [InlineData(new string[] { "--extract-here", "archive.zip" }, QuickExtractMode.ExtractHere, "archive.zip", null)]
    [InlineData(new string[] { "--extract-here=archive.zrus" }, QuickExtractMode.ExtractHere, "archive.zrus", null)]
    [InlineData(new string[] { "--extract-here", "archive.tar.zstd" }, QuickExtractMode.ExtractHere, "archive.tar.zstd", null)]
    [InlineData(new string[] { "--extract-here=archive.tzstd" }, QuickExtractMode.ExtractHere, "archive.tzstd", null)]
    [InlineData(new string[] { "--extract-here", "archive.zst" }, QuickExtractMode.ExtractHere, "archive.zst", null)]
    [InlineData(new string[] { "--EXTRACT-HERE", "archive.tar.gz" }, QuickExtractMode.ExtractHere, "archive.tar.gz", null)]
    [InlineData(new string[] { "--extract-to-dir", "archive.tar.gz" }, QuickExtractMode.ExtractToDir, "archive.tar.gz", null)]
    [InlineData(new string[] { "--extract-to-dir=archive.tar.zstd" }, QuickExtractMode.ExtractToDir, "archive.tar.zstd", null)]
    [InlineData(new string[] { "--extract-to-subfolder", "archive.tzstd" }, QuickExtractMode.ExtractToDir, "archive.tzstd", null)]
    [InlineData(new string[] { "--extract-to-dir", "archive.zst" }, QuickExtractMode.ExtractToDir, "archive.zst", null)]
    [InlineData(new string[] { "--extract-to-dir=archive.7z" }, QuickExtractMode.ExtractToDir, "archive.7z", null)]
    [InlineData(new string[] { "--extract-to-subfolder", "archive.rar" }, QuickExtractMode.ExtractToDir, "archive.rar", null)]
    [InlineData(new string[] { "--extract-to", "archive.zip" }, QuickExtractMode.ExtractTo, "archive.zip", null)]
    [InlineData(new string[] { "--extract-to=archive.zip" }, QuickExtractMode.ExtractTo, "archive.zip", null)]
    [InlineData(new string[] { "--extract-to", "archive.tar.zstd", "/dest/folder" }, QuickExtractMode.ExtractTo, "archive.tar.zstd", "/dest/folder")]
    [InlineData(new string[] { "--extract-to", "archive.zst", "/dest/folder" }, QuickExtractMode.ExtractTo, "archive.zst", "/dest/folder")]
    [InlineData(new string[] { "--extract-to", "archive.zip", "/dest/folder" }, QuickExtractMode.ExtractTo, "archive.zip", "/dest/folder")]
    [InlineData(new string[] { "--EXTRACT-TO", "archive.zip", "/custom/path" }, QuickExtractMode.ExtractTo, "archive.zip", "/custom/path")]
    public void Parse_ValidArguments_ReturnsExpectedOptions(string[] args, QuickExtractMode expectedMode, string expectedArchive, string? expectedDest)
    {
        var result = QuickExtractCommandLineParser.Parse(args);

        Assert.NotNull(result);
        Assert.Equal(expectedMode, result.Mode);
        Assert.Equal(expectedArchive, result.ArchivePath);
        Assert.Equal(expectedDest, result.DestinationDirectory);
    }

    [Fact]
    public void Parse_NullOrEmptyArgs_ReturnsNull()
    {
        Assert.Null(QuickExtractCommandLineParser.Parse(null));
        Assert.Null(QuickExtractCommandLineParser.Parse([]));
        Assert.Null(QuickExtractCommandLineParser.Parse(["   ", ""]));
    }

    [Fact]
    public void Parse_UnrelatedArgs_ReturnsNull()
    {
        Assert.Null(QuickExtractCommandLineParser.Parse(["--version"]));
        Assert.Null(QuickExtractCommandLineParser.Parse(["--help"]));
        Assert.Null(QuickExtractCommandLineParser.Parse(["/path/to/archive.zip"]));
    }

    [Fact]
    public void Parse_ExtractHereWithoutPath_ReturnsNull()
    {
        Assert.Null(QuickExtractCommandLineParser.Parse(["--extract-here"]));
    }
}

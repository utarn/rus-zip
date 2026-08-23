using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

public class ArchiveFormatRegistryTests
{
    [Theory]
    [InlineData("backup.zrus", ArchiveFormat.Zrus, true, true)]
    [InlineData("archive.zip", ArchiveFormat.Zip, true, true)]
    [InlineData("data.7z", ArchiveFormat.SevenZip, false, true)]
    [InlineData("legacy.rar", ArchiveFormat.Rar, false, true)]
    [InlineData("stream.gz", ArchiveFormat.Gz, false, true)]
    [InlineData("stream.zst", ArchiveFormat.Zst, true, true)]
    [InlineData("package.tar.gz", ArchiveFormat.TarGz, false, true)]
    [InlineData("bundle.tgz", ArchiveFormat.TarGz, false, true)]
    [InlineData("backup.tar.zstd", ArchiveFormat.Zrus, true, true)]
    [InlineData("backup.tzstd", ArchiveFormat.Zrus, true, true)]
    [InlineData("UPPER.ZRUS", ArchiveFormat.Zrus, true, true)]
    [InlineData("UPPER.ZST", ArchiveFormat.Zst, true, true)]
    [InlineData("TEST.TAR.GZ", ArchiveFormat.TarGz, false, true)]
    [InlineData("UPPER.TAR.ZSTD", ArchiveFormat.Zrus, true, true)]
    [InlineData("UPPER.TZSTD", ArchiveFormat.Zrus, true, true)]
    [InlineData(".zrus", ArchiveFormat.Zrus, true, true)]
    [InlineData(".zip", ArchiveFormat.Zip, true, true)]
    [InlineData(".zst", ArchiveFormat.Zst, true, true)]
    [InlineData(".tar.gz", ArchiveFormat.TarGz, false, true)]
    [InlineData(".tgz", ArchiveFormat.TarGz, false, true)]
    [InlineData(".tar.zstd", ArchiveFormat.Zrus, true, true)]
    [InlineData(".tzstd", ArchiveFormat.Zrus, true, true)]
    [InlineData("zrus", ArchiveFormat.Zrus, true, true)]
    [InlineData("zip", ArchiveFormat.Zip, true, true)]
    [InlineData("zst", ArchiveFormat.Zst, true, true)]
    [InlineData("tar.gz", ArchiveFormat.TarGz, false, true)]
    [InlineData("tgz", ArchiveFormat.TarGz, false, true)]
    [InlineData("tar.zstd", ArchiveFormat.Zrus, true, true)]
    [InlineData("tzstd", ArchiveFormat.Zrus, true, true)]
    public void Detect_ResolvesCorrectFormatAndCapabilities(string path, ArchiveFormat expectedFormat, bool canCompress, bool canDecompress)
    {
        var descriptor = ArchiveFormatRegistry.Detect(path);

        Assert.Equal(expectedFormat, descriptor.Format);
        Assert.Equal(canCompress, descriptor.CanCompress);
        Assert.Equal(canDecompress, descriptor.CanDecompress);
    }

    [Theory]
    [InlineData("file.txt")]
    [InlineData("image.png")]
    [InlineData("unknown.xyz")]
    [InlineData("tar")]
    [InlineData(".tar")]
    [InlineData("")]
    [InlineData("   ")]
    public void Detect_UnsupportedExtension_ThrowsNotSupportedException(string path)
    {
        var ex = Assert.Throws<NotSupportedException>(() => ArchiveFormatRegistry.Detect(path));
        Assert.Contains("Unsupported archive format", ex.Message);
    }

    [Theory]
    [InlineData("test.zrus", true, ArchiveFormat.Zrus)]
    [InlineData("test.zip", true, ArchiveFormat.Zip)]
    [InlineData("test.zst", true, ArchiveFormat.Zst)]
    [InlineData("test.tar.gz", true, ArchiveFormat.TarGz)]
    [InlineData("test.tgz", true, ArchiveFormat.TarGz)]
    [InlineData("test.tar.zstd", true, ArchiveFormat.Zrus)]
    [InlineData("test.tzstd", true, ArchiveFormat.Zrus)]
    [InlineData("test.7z", true, ArchiveFormat.SevenZip)]
    [InlineData("test.rar", true, ArchiveFormat.Rar)]
    [InlineData("test.gz", true, ArchiveFormat.Gz)]
    [InlineData("TEST.ZRUS", true, ArchiveFormat.Zrus)]
    [InlineData("TEST.ZST", true, ArchiveFormat.Zst)]
    [InlineData("archive.TAR.GZ", true, ArchiveFormat.TarGz)]
    [InlineData("archive.TAR.ZSTD", true, ArchiveFormat.Zrus)]
    [InlineData("archive.TZSTD", true, ArchiveFormat.Zrus)]
    [InlineData("test.pdf", false, null)]
    [InlineData("test.tar", false, null)]
    [InlineData("", false, null)]
    [InlineData(null, false, null)]
    [InlineData("   ", false, null)]
    public void TryDetect_IdentifiesFormatOrReturnsFalse(string? path, bool expectedSuccess, ArchiveFormat? expectedFormat)
    {
        var success = ArchiveFormatRegistry.TryDetect(path, out var descriptor);

        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.NotNull(descriptor);
            Assert.Equal(expectedFormat, descriptor.Format);
        }
        else
        {
            Assert.Null(descriptor);
        }
    }

    [Theory]
    [InlineData("test.zrus", true)]
    [InlineData("test.zip", true)]
    [InlineData("test.zst", true)]
    [InlineData("test.tar.gz", true)]
    [InlineData("test.tgz", true)]
    [InlineData("test.tar.zstd", true)]
    [InlineData("test.tzstd", true)]
    [InlineData("test.7z", true)]
    [InlineData("test.rar", true)]
    [InlineData("test.gz", true)]
    [InlineData("test.pdf", false)]
    [InlineData("test.tar", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupportedArchive_IdentifiesSupportedFiles(string? path, bool expected)
    {
        var result = ArchiveFormatRegistry.IsSupportedArchive(path);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CompressibleFormats_ContainsZrusZipAndZst()
    {
        var formats = ArchiveFormatRegistry.CompressibleFormats;

        Assert.Equal(3, formats.Count);
        Assert.Contains(formats, f => f.Format == ArchiveFormat.Zrus);
        Assert.Contains(formats, f => f.Format == ArchiveFormat.Zip);
        Assert.Contains(formats, f => f.Format == ArchiveFormat.Zst);
        Assert.All(formats, f => Assert.True(f.CanCompress));
    }

    [Fact]
    public void DecompressibleFormats_ContainsAllSevenFormats()
    {
        var formats = ArchiveFormatRegistry.DecompressibleFormats;

        Assert.Equal(7, formats.Count);
        Assert.All(formats, f => Assert.True(f.CanDecompress));
    }

    [Fact]
    public void SupportedExtensions_ContainsAllExpectedExtensions()
    {
        var extensions = ArchiveFormatRegistry.SupportedExtensions;

        Assert.Contains(".zrus", extensions);
        Assert.Contains(".tar.zstd", extensions);
        Assert.Contains(".tzstd", extensions);
        Assert.Contains(".zip", extensions);
        Assert.Contains(".zst", extensions);
        Assert.Contains(".tar.gz", extensions);
        Assert.Contains(".tgz", extensions);
        Assert.Contains(".7z", extensions);
        Assert.Contains(".rar", extensions);
        Assert.Contains(".gz", extensions);
        Assert.Equal(10, extensions.Count);
    }

    [Fact]
    public void Formats_ContainsAllSevenDescriptorsWithMetadata()
    {
        var formats = ArchiveFormatRegistry.Formats;

        Assert.Equal(7, formats.Count);

        var zrus = Assert.Single(formats, f => f.Format == ArchiveFormat.Zrus);
        Assert.Equal(".zrus", zrus.PrimaryExtension);
        Assert.Contains(".zrus", zrus.Extensions);
        Assert.Contains(".tar.zstd", zrus.Extensions);
        Assert.Contains(".tzstd", zrus.Extensions);
        Assert.Equal(1, zrus.MinCompressionLevel);
        Assert.Equal(22, zrus.MaxCompressionLevel);
        Assert.Equal(9, zrus.DefaultCompressionLevel);
        Assert.Equal("application/x-zstd-tar", zrus.MimeType);

        var zip = Assert.Single(formats, f => f.Format == ArchiveFormat.Zip);
        Assert.Equal(".zip", zip.PrimaryExtension);
        Assert.Equal(0, zip.MinCompressionLevel);
        Assert.Equal(9, zip.MaxCompressionLevel);
        Assert.Equal(6, zip.DefaultCompressionLevel);
        Assert.Equal("application/zip", zip.MimeType);

        var zst = Assert.Single(formats, f => f.Format == ArchiveFormat.Zst);
        Assert.Equal(".zst", zst.PrimaryExtension);
        Assert.Equal(1, zst.MinCompressionLevel);
        Assert.Equal(22, zst.MaxCompressionLevel);
        Assert.Equal(9, zst.DefaultCompressionLevel);
        Assert.Equal("application/zstd", zst.MimeType);
        Assert.True(zst.CanCompress);
        Assert.True(zst.CanDecompress);

        var tarGz = Assert.Single(formats, f => f.Format == ArchiveFormat.TarGz);
        Assert.Equal(".tar.gz", tarGz.PrimaryExtension);
        Assert.Contains(".tar.gz", tarGz.Extensions);
        Assert.Contains(".tgz", tarGz.Extensions);
        Assert.False(tarGz.CanCompress);
        Assert.True(tarGz.CanDecompress);
    }

    [Fact]
    public void MatchesExtension_DescriptorMethod_CorrectlyIdentifies()
    {
        var zrus = ArchiveFormatRegistry.Zrus;
        Assert.True(zrus.MatchesExtension(".zrus"));
        Assert.True(zrus.MatchesExtension(".tar.zstd"));
        Assert.True(zrus.MatchesExtension(".tzstd"));
        Assert.True(zrus.MatchesExtension("zrus"));
        Assert.True(zrus.MatchesExtension("tar.zstd"));
        Assert.True(zrus.MatchesExtension("tzstd"));
        Assert.True(zrus.MatchesExtension("folder/file.zrus"));
        Assert.True(zrus.MatchesExtension("/path/to/archive.tar.zstd"));
        Assert.True(zrus.MatchesExtension("/path/to/archive.tzstd"));
        Assert.False(zrus.MatchesExtension(".zip"));

        var tarGz = ArchiveFormatRegistry.TarGz;
        Assert.True(tarGz.MatchesExtension(".tar.gz"));
        Assert.True(tarGz.MatchesExtension(".tgz"));
        Assert.True(tarGz.MatchesExtension("tar.gz"));
        Assert.True(tarGz.MatchesExtension("tgz"));
        Assert.True(tarGz.MatchesExtension("/path/to/archive.tar.gz"));
        Assert.True(tarGz.MatchesExtension("/path/to/archive.tgz"));
        Assert.False(tarGz.MatchesExtension(".gz"));
        Assert.False(tarGz.MatchesExtension(""));
    }
}

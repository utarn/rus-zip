using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Tests;

public class FileIconCategorizerTests
{
    [Theory]
    // Formats the ArchiveFormatRegistry actually recognizes (capability-backed).
    [InlineData("archive.zrus")]
    [InlineData("archive.zip")]
    [InlineData("archive.7z")]
    [InlineData("archive.rar")]
    [InlineData("stream.gz")]
    [InlineData("package.tar.gz")]
    [InlineData("bundle.tgz")]
    [InlineData("UPPER.ZRUS")]
    [InlineData("ARCHIVE.TAR.GZ")]
    public void IsArchiveFile_RegistryRecognizedFormats_ReturnsTrue(string fileName)
    {
        Assert.True(FileIconCategorizer.IsArchiveFile(fileName));
    }

    [Theory]
    // Presentation-only aliases: well-known archive extensions NOT in the registry.
    [InlineData("file.tar")]
    [InlineData("file.bz2")]
    [InlineData("file.xz")]
    [InlineData("file.cab")]
    [InlineData("file.iso")]
    [InlineData("file.7zip")]
    [InlineData("file.tbz2")]
    [InlineData("file.txz")]
    [InlineData("file.tar.bz2")]
    [InlineData("file.tar.xz")]
    [InlineData("FILE.ISO")]
    public void IsArchiveFile_AliasNonRegistryExtensions_ReturnsTrue(string fileName)
    {
        Assert.True(FileIconCategorizer.IsArchiveFile(fileName));
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("code.cs")]
    [InlineData("image.png")]
    [InlineData("app.exe")]
    [InlineData("unknown.xyz")]
    [InlineData("unknown_file")]
    [InlineData("Dockerfile")]
    [InlineData("")]
    [InlineData(null)]
    public void IsArchiveFile_UnknownOrEmpty_ReturnsFalse(string? fileName)
    {
        Assert.False(FileIconCategorizer.IsArchiveFile(fileName));
    }

    [Theory]
    // Registry-recognized archives.
    [InlineData("archive.zrus", "📦")]
    [InlineData("archive.zip", "📦")]
    [InlineData("archive.rar", "📦")]
    [InlineData("archive.tar.gz", "📦")]
    [InlineData("archive.tgz", "📦")]
    // Presentation aliases.
    [InlineData("archive.tar", "📦")]
    [InlineData("archive.iso", "📦")]
    [InlineData("archive.bz2", "📦")]
    // Non-archives still map to their own category.
    [InlineData("code.cs", "📝")]
    [InlineData("image.png", "🖼️")]
    [InlineData("doc.pdf", "📄")]
    [InlineData("app.exe", "⚙️")]
    [InlineData("song.mp3", "🎬")]
    public void GetFileIcon_ClassifiesArchivesViaRegistryOrAlias(string fileName, string expectedIcon)
    {
        Assert.Equal(expectedIcon, FileIconCategorizer.GetFileIcon(fileName));
    }

    [Theory]
    // Registry-recognized archives.
    [InlineData("archive.zrus", "Icon.FileArchive")]
    [InlineData("archive.zip", "Icon.FileArchive")]
    [InlineData("archive.7z", "Icon.FileArchive")]
    [InlineData("archive.rar", "Icon.FileArchive")]
    [InlineData("archive.gz", "Icon.FileArchive")]
    [InlineData("archive.tar.gz", "Icon.FileArchive")]
    [InlineData("archive.tgz", "Icon.FileArchive")]
    // Presentation aliases.
    [InlineData("archive.tar", "Icon.FileArchive")]
    [InlineData("archive.iso", "Icon.FileArchive")]
    [InlineData("archive.bz2", "Icon.FileArchive")]
    [InlineData("archive.xz", "Icon.FileArchive")]
    [InlineData("archive.cab", "Icon.FileArchive")]
    [InlineData("archive.7zip", "Icon.FileArchive")]
    [InlineData("archive.tbz2", "Icon.FileArchive")]
    [InlineData("archive.txz", "Icon.FileArchive")]
    // Non-archives still map to their own category.
    [InlineData("code.cs", "Icon.FileCode")]
    [InlineData("image.png", "Icon.FileImage")]
    [InlineData("doc.pdf", "Icon.FileDoc")]
    [InlineData("app.exe", "Icon.FileGeneric")]
    public void GetIconKey_ClassifiesArchivesViaRegistryOrAlias(string fileName, string expectedKey)
    {
        Assert.Equal(expectedKey, FileIconCategorizer.GetIconKey(fileName));
    }

    [Fact]
    public void GetIconKey_ForDirectory_ReturnsFolderKey()
    {
        Assert.Equal("Icon.Folder", FileIconCategorizer.GetIconKey("anything", isDirectory: true));
    }
}

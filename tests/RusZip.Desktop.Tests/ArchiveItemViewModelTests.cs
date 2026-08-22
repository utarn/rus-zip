using System.ComponentModel;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Tests;

public class ArchiveItemViewModelTests
{
    [Theory]
    [InlineData(-10, "0 B")]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(1099511627776, "1 TB")]
    public void FormatBytes_FormatsCorrectly(long bytes, string expected)
    {
        var formatted = ArchiveItemViewModel.FormatBytes(bytes);
        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData("archive.zrus", "📦")]
    [InlineData("archive.zip", "📦")]
    [InlineData("archive.rar", "📦")]
    [InlineData("archive.7z", "📦")]
    [InlineData("archive.tar.gz", "📦")]
    [InlineData("archive.tgz", "📦")]
    [InlineData("code.cs", "📝")]
    [InlineData("script.py", "📝")]
    [InlineData("config.json", "📝")]
    [InlineData("doc.md", "📄")]
    [InlineData("image.png", "🖼️")]
    [InlineData("image.jpg", "🖼️")]
    [InlineData("app.exe", "⚙️")]
    [InlineData("library.dll", "⚙️")]
    [InlineData("song.mp3", "🎬")]
    [InlineData("video.mp4", "🎬")]
    [InlineData("unknown.xyz", "📄")]
    public void GetFileIcon_ReturnsExpectedIcon(string filename, string expectedIcon)
    {
        var icon = ArchiveItemViewModel.GetFileIcon(filename);
        Assert.Equal(expectedIcon, icon);
    }

    [Fact]
    public void Directory_DisplaysFolderIconAndDashesForSizes()
    {
        var dirItem = new ArchiveItemViewModel
        {
            Name = "MyFolder",
            RelativePath = "MyFolder",
            ItemType = ArchiveItemType.Directory,
            UncompressedSize = 1048576,
            CompressedSize = 524288
        };

        Assert.True(dirItem.IsDirectory);
        Assert.Equal("📁", dirItem.IconDisplay);
        Assert.Equal("-", dirItem.FormattedUncompressedSize);
        Assert.Equal("-", dirItem.FormattedCompressedSize);
        Assert.Equal("-", dirItem.FormattedRatio);
    }

    [Fact]
    public void File_DisplaysFormattedSizesAndRatio()
    {
        var fileItem = new ArchiveItemViewModel
        {
            Name = "document.pdf",
            RelativePath = "docs/document.pdf",
            ItemType = ArchiveItemType.File,
            UncompressedSize = 2000,
            CompressedSize = 1000,
            LastModified = new DateTimeOffset(2026, 8, 22, 10, 30, 0, TimeSpan.Zero)
        };

        Assert.False(fileItem.IsDirectory);
        Assert.Equal("📄", fileItem.IconDisplay);
        Assert.Equal("1.95 KB", fileItem.FormattedUncompressedSize);
        Assert.Equal("1000 B", fileItem.FormattedCompressedSize);
        Assert.Equal("50.0%", fileItem.FormattedRatio);
        Assert.Equal("2026-08-22 10:30", fileItem.FormattedLastModified);
    }

    [Fact]
    public void File_WithNullCompressedSize_DisplaysDashesForCompressedAndRatio()
    {
        var fileItem = new ArchiveItemViewModel
        {
            Name = "raw.bin",
            ItemType = ArchiveItemType.File,
            UncompressedSize = 1024,
            CompressedSize = null,
            LastModified = null
        };

        Assert.Equal("1 KB", fileItem.FormattedUncompressedSize);
        Assert.Equal("-", fileItem.FormattedCompressedSize);
        Assert.Equal("-", fileItem.FormattedRatio);
        Assert.Equal("-", fileItem.FormattedLastModified);
    }

    [Fact]
    public void ChildrenAndHasChildren_WorksCorrectly()
    {
        var parent = new ArchiveItemViewModel
        {
            Name = "Parent",
            ItemType = ArchiveItemType.Directory
        };

        Assert.False(parent.HasChildren);
        Assert.Empty(parent.Children);

        var child = new ArchiveItemViewModel
        {
            Name = "Child.txt",
            ItemType = ArchiveItemType.File
        };

        parent.Children.Add(child);
        Assert.True(parent.HasChildren);
        Assert.Single(parent.Children);
        Assert.Same(child, parent.Children[0]);
    }

    [Fact]
    public void ObservableProperties_NotifyChanges()
    {
        var item = new ArchiveItemViewModel();
        var changedProps = new List<string>();
        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) changedProps.Add(e.PropertyName);
        };

        item.Name = "test.txt";
        item.RelativePath = "folder/test.txt";
        item.ItemType = ArchiveItemType.File;
        item.UncompressedSize = 100;
        item.CompressedSize = 50;
        item.LastModified = DateTimeOffset.UtcNow;
        item.Attributes = "rw-r--r--";
        item.IsExpanded = true;

        Assert.Contains(nameof(ArchiveItemViewModel.Name), changedProps);
        Assert.Contains(nameof(ArchiveItemViewModel.RelativePath), changedProps);
        Assert.Contains(nameof(ArchiveItemViewModel.ItemType), changedProps);
        Assert.Contains(nameof(ArchiveItemViewModel.UncompressedSize), changedProps);
        Assert.Contains(nameof(ArchiveItemViewModel.CompressedSize), changedProps);
        Assert.Contains(nameof(ArchiveItemViewModel.LastModified), changedProps);
        Assert.Contains(nameof(ArchiveItemViewModel.Attributes), changedProps);
        Assert.Contains(nameof(ArchiveItemViewModel.IsExpanded), changedProps);
    }

    [Theory]
    [InlineData("test.cs", "Icon.FileCode")]
    [InlineData("test.rs", "Icon.FileCode")]
    [InlineData("test.py", "Icon.FileCode")]
    [InlineData("test.js", "Icon.FileCode")]
    [InlineData("test.ts", "Icon.FileCode")]
    [InlineData("test.json", "Icon.FileCode")]
    [InlineData("test.xml", "Icon.FileCode")]
    [InlineData("test.yaml", "Icon.FileCode")]
    [InlineData("test.yml", "Icon.FileCode")]
    [InlineData("test.toml", "Icon.FileCode")]
    [InlineData("test.html", "Icon.FileCode")]
    [InlineData("test.css", "Icon.FileCode")]
    [InlineData("test.sh", "Icon.FileCode")]
    [InlineData("test.cpp", "Icon.FileCode")]
    [InlineData("test.h", "Icon.FileCode")]
    [InlineData("test.txt", "Icon.FileDoc")]
    [InlineData("test.md", "Icon.FileDoc")]
    [InlineData("test.pdf", "Icon.FileDoc")]
    [InlineData("test.doc", "Icon.FileDoc")]
    [InlineData("test.docx", "Icon.FileDoc")]
    [InlineData("test.rtf", "Icon.FileDoc")]
    [InlineData("test.log", "Icon.FileDoc")]
    [InlineData("test.csv", "Icon.FileDoc")]
    [InlineData("test.png", "Icon.FileImage")]
    [InlineData("test.jpg", "Icon.FileImage")]
    [InlineData("test.jpeg", "Icon.FileImage")]
    [InlineData("test.svg", "Icon.FileImage")]
    [InlineData("test.webp", "Icon.FileImage")]
    [InlineData("test.ico", "Icon.FileImage")]
    [InlineData("test.gif", "Icon.FileImage")]
    [InlineData("test.bmp", "Icon.FileImage")]
    [InlineData("test.zrus", "Icon.FileArchive")]
    [InlineData("test.zip", "Icon.FileArchive")]
    [InlineData("test.tar", "Icon.FileArchive")]
    [InlineData("test.gz", "Icon.FileArchive")]
    [InlineData("test.tgz", "Icon.FileArchive")]
    [InlineData("test.7z", "Icon.FileArchive")]
    [InlineData("test.rar", "Icon.FileArchive")]
    [InlineData("test.bz2", "Icon.FileArchive")]
    [InlineData("test.xz", "Icon.FileArchive")]
    [InlineData("test.exe", "Icon.FileGeneric")]
    [InlineData("test.bin", "Icon.FileGeneric")]
    [InlineData("unknown_file", "Icon.FileGeneric")]
    public void GetIconKey_ReturnsExpectedResourceKey(string filename, string expectedKey)
    {
        var key = ArchiveItemViewModel.GetIconKey(filename, isDirectory: false);
        Assert.Equal(expectedKey, key);
    }

    [Fact]
    public void GetIconKey_ForDirectory_ReturnsFolderKey()
    {
        var key = ArchiveItemViewModel.GetIconKey("some_folder", isDirectory: true);
        Assert.Equal("Icon.Folder", key);
    }

    [Fact]
    public void IconKey_OnViewModel_ReturnsMatchingKey()
    {
        var dirItem = new ArchiveItemViewModel
        {
            Name = "DirectoryName",
            ItemType = ArchiveItemType.Directory
        };
        Assert.Equal("Icon.Folder", dirItem.IconKey);

        var fileItem = new ArchiveItemViewModel
        {
            Name = "App.cs",
            ItemType = ArchiveItemType.File
        };
        Assert.Equal("Icon.FileCode", fileItem.IconKey);
    }

    [Fact]
    public void IconGeometry_WhenApplicationCurrentNull_ReturnsNullGracefully()
    {
        var fileItem = new ArchiveItemViewModel
        {
            Name = "document.pdf",
            ItemType = ArchiveItemType.File
        };

        var geom = fileItem.IconGeometry;
        Assert.Null(geom);
    }
}

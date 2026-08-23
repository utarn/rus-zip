using System.ComponentModel;
using RusZip.Core.Models;
using RusZip.Desktop.ViewModels;

namespace RusZip.Desktop.Tests;

public class ArchiveItemViewModelTests
{
    [Theory]
    [InlineData("archive.zrus", "📦")]
    [InlineData("archive.tar.zstd", "📦")]
    [InlineData("archive.tzstd", "📦")]
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
        Assert.Equal("2.0 KB", fileItem.FormattedUncompressedSize);
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

        Assert.Equal("1.0 KB", fileItem.FormattedUncompressedSize);
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
    // Code
    [InlineData("test.cs", "Icon.FileCode")]
    [InlineData("test.CS", "Icon.FileCode")]
    [InlineData("test.rs", "Icon.FileCode")]
    [InlineData("test.py", "Icon.FileCode")]
    [InlineData("test.js", "Icon.FileCode")]
    [InlineData("test.ts", "Icon.FileCode")]
    [InlineData("test.jsx", "Icon.FileCode")]
    [InlineData("test.tsx", "Icon.FileCode")]
    [InlineData("test.vue", "Icon.FileCode")]
    [InlineData("test.svelte", "Icon.FileCode")]
    [InlineData("test.json", "Icon.FileCode")]
    [InlineData("test.xml", "Icon.FileCode")]
    [InlineData("test.yaml", "Icon.FileCode")]
    [InlineData("test.yml", "Icon.FileCode")]
    [InlineData("test.toml", "Icon.FileCode")]
    [InlineData("test.html", "Icon.FileCode")]
    [InlineData("test.css", "Icon.FileCode")]
    [InlineData("test.scss", "Icon.FileCode")]
    [InlineData("test.sh", "Icon.FileCode")]
    [InlineData("test.bash", "Icon.FileCode")]
    [InlineData("test.zsh", "Icon.FileCode")]
    [InlineData("test.cpp", "Icon.FileCode")]
    [InlineData("test.c", "Icon.FileCode")]
    [InlineData("test.h", "Icon.FileCode")]
    [InlineData("test.hpp", "Icon.FileCode")]
    [InlineData("test.go", "Icon.FileCode")]
    [InlineData("test.java", "Icon.FileCode")]
    [InlineData("test.kt", "Icon.FileCode")]
    [InlineData("test.kts", "Icon.FileCode")]
    [InlineData("test.swift", "Icon.FileCode")]
    [InlineData("test.php", "Icon.FileCode")]
    [InlineData("test.rb", "Icon.FileCode")]
    [InlineData("test.lua", "Icon.FileCode")]
    [InlineData("test.sql", "Icon.FileCode")]
    [InlineData("test.ps1", "Icon.FileCode")]
    [InlineData("test.bat", "Icon.FileCode")]
    // Document
    [InlineData("test.txt", "Icon.FileDoc")]
    [InlineData("test.md", "Icon.FileDoc")]
    [InlineData("test.pdf", "Icon.FileDoc")]
    [InlineData("test.PDF", "Icon.FileDoc")]
    [InlineData("test.doc", "Icon.FileDoc")]
    [InlineData("test.docx", "Icon.FileDoc")]
    [InlineData("test.rtf", "Icon.FileDoc")]
    [InlineData("test.log", "Icon.FileDoc")]
    [InlineData("test.csv", "Icon.FileDoc")]
    [InlineData("test.odt", "Icon.FileDoc")]
    [InlineData("test.xlsx", "Icon.FileDoc")]
    [InlineData("test.xls", "Icon.FileDoc")]
    [InlineData("test.pptx", "Icon.FileDoc")]
    [InlineData("test.ppt", "Icon.FileDoc")]
    // Image
    [InlineData("test.png", "Icon.FileImage")]
    [InlineData("test.PNG", "Icon.FileImage")]
    [InlineData("test.jpg", "Icon.FileImage")]
    [InlineData("test.jpeg", "Icon.FileImage")]
    [InlineData("test.svg", "Icon.FileImage")]
    [InlineData("test.webp", "Icon.FileImage")]
    [InlineData("test.ico", "Icon.FileImage")]
    [InlineData("test.gif", "Icon.FileImage")]
    [InlineData("test.bmp", "Icon.FileImage")]
    [InlineData("test.tiff", "Icon.FileImage")]
    [InlineData("test.tif", "Icon.FileImage")]
    [InlineData("test.heic", "Icon.FileImage")]
    // Archive
    [InlineData("test.zrus", "Icon.FileArchive")]
    [InlineData("test.tar.zstd", "Icon.FileArchive")]
    [InlineData("test.tzstd", "Icon.FileArchive")]
    [InlineData("test.zip", "Icon.FileArchive")]
    [InlineData("test.ZIP", "Icon.FileArchive")]
    [InlineData("test.tar", "Icon.FileArchive")]
    [InlineData("test.gz", "Icon.FileArchive")]
    [InlineData("test.tgz", "Icon.FileArchive")]
    [InlineData("test.7z", "Icon.FileArchive")]
    [InlineData("test.rar", "Icon.FileArchive")]
    [InlineData("test.bz2", "Icon.FileArchive")]
    [InlineData("test.xz", "Icon.FileArchive")]
    [InlineData("test.cab", "Icon.FileArchive")]
    [InlineData("test.iso", "Icon.FileArchive")]
    [InlineData("test.ISO", "Icon.FileArchive")]
    // Generic / Fallback
    [InlineData("test.exe", "Icon.FileGeneric")]
    [InlineData("test.bin", "Icon.FileGeneric")]
    [InlineData("unknown_file", "Icon.FileGeneric")]
    [InlineData("Dockerfile", "Icon.FileGeneric")]
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

    [Fact]
    public void FromTreeNode_MapsDirectoryAndChildren_Correctly()
    {
        var date = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        var rootNode = new ArchiveTreeNode
        {
            Name = "src",
            RelativePath = "src",
            IsDirectory = true,
            UncompressedSize = 1500,
            CompressedSize = 750,
            LastModified = date,
            Attributes = "rwxr-xr-x"
        };
        var childNode = new ArchiveTreeNode
        {
            Name = "main.cs",
            RelativePath = "src/main.cs",
            IsDirectory = false,
            UncompressedSize = 1500,
            CompressedSize = 750,
            LastModified = date,
            Attributes = "rw-r--r--"
        };
        rootNode.Children.Add(childNode);

        var vm = ArchiveItemViewModel.FromTreeNode(rootNode, autoExpand: true);

        Assert.Equal("src", vm.Name);
        Assert.Equal("src", vm.RelativePath);
        Assert.True(vm.IsDirectory);
        Assert.Equal(ArchiveItemType.Directory, vm.ItemType);
        Assert.Equal(1500, vm.UncompressedSize);
        Assert.Equal(750, vm.CompressedSize);
        Assert.Equal(date, vm.LastModified);
        Assert.Equal("rwxr-xr-x", vm.Attributes);
        Assert.True(vm.IsExpanded);
        Assert.Single(vm.Children);

        var childVm = vm.Children[0];
        Assert.Equal("main.cs", childVm.Name);
        Assert.Equal("src/main.cs", childVm.RelativePath);
        Assert.False(childVm.IsDirectory);
        Assert.Equal(ArchiveItemType.File, childVm.ItemType);
        Assert.Equal(1500, childVm.UncompressedSize);
        Assert.Equal(750, childVm.CompressedSize);
        Assert.Equal(date, childVm.LastModified);
        Assert.Equal("rw-r--r--", childVm.Attributes);
        Assert.True(childVm.IsExpanded);
    }

    [Fact]
    public void FromTreeNode_SanitizesControlBytesInDisplayFields()
    {
        char esc = (char)0x1b;
        char nul = (char)0x00;
        var node = new ArchiveTreeNode
        {
            Name = $"ok{esc}[31mRED{esc}[0m{nul}file.txt",
            RelativePath = $"folder/ok{esc}[31mRED{esc}[0m{nul}file.txt",
            IsDirectory = false,
            Attributes = $"rw-{esc}r--r--"
        };

        var vm = ArchiveItemViewModel.FromTreeNode(node);

        Assert.Equal("ok[31mRED[0mfile.txt", vm.Name);
        Assert.Equal("folder/ok[31mRED[0mfile.txt", vm.RelativePath);
        Assert.Equal("rw-r--r--", vm.Attributes);
        Assert.DoesNotContain(esc, vm.Name);
        Assert.DoesNotContain(nul, vm.Name);
    }
}

using System.ComponentModel;
using RusZip.Desktop.ViewModels;
using Xunit;

namespace RusZip.Desktop.Tests;

public class StagedSourceItemViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public StagedSourceItemViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "staged_item_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { /* Ignore */ }
        }
    }

    [Fact]
    public void DefaultConstructor_InitializesEmptyValues()
    {
        var vm = new StagedSourceItemViewModel();

        Assert.Equal(string.Empty, vm.Name);
        Assert.Equal(string.Empty, vm.FullPath);
        Assert.Equal(string.Empty, vm.RelativePath);
        Assert.False(vm.IsDirectory);
        Assert.Equal(0, vm.Size);
        Assert.Null(vm.LastModified);
        Assert.Equal(string.Empty, vm.Attributes);
        Assert.False(vm.IsExcluded);
        Assert.False(vm.IsExpanded);
        Assert.Null(vm.Parent);
        Assert.Empty(vm.Children);
        Assert.False(vm.HasChildren);
        Assert.Equal("0 B", vm.FormattedSize);
        Assert.Equal("0 B", vm.FormattedUncompressedSize);
        Assert.Equal("-", vm.FormattedLastModified);
        Assert.Equal("Icon.FileGeneric", vm.IconKey);
        Assert.Equal("📄", vm.IconDisplay);
    }

    [Fact]
    public void FromFileSystem_WithFile_PopulatesMetadataCorrectly()
    {
        var filePath = Path.Combine(_tempDir, "document.txt");
        File.WriteAllText(filePath, "Hello, world! 12345");

        var vm = StagedSourceItemViewModel.FromFileSystem(filePath);

        Assert.Equal("document.txt", vm.Name);
        Assert.Equal(Path.GetFullPath(filePath), vm.FullPath);
        Assert.Equal("document.txt", vm.RelativePath);
        Assert.False(vm.IsDirectory);
        Assert.Equal(new FileInfo(filePath).Length, vm.Size);
        Assert.NotNull(vm.LastModified);
        Assert.NotEmpty(vm.Attributes);
        Assert.False(vm.IsExcluded);
        Assert.Null(vm.Parent);
        Assert.Empty(vm.Children);
        Assert.Equal("Icon.FileDoc", vm.IconKey);
        Assert.Equal("📄", vm.IconDisplay);
        Assert.NotEqual("-", vm.FormattedLastModified);
        Assert.Equal("19 B", vm.FormattedSize);
    }

    [Fact]
    public void FromFileSystem_WithDirectoryTree_BuildsHierarchyAndAggregatesSize()
    {
        var subDir = Path.Combine(_tempDir, "sub_folder");
        Directory.CreateDirectory(subDir);
        var file1 = Path.Combine(_tempDir, "file1.txt");
        var file2 = Path.Combine(subDir, "file2.json");
        File.WriteAllText(file1, "12345"); // 5 bytes
        File.WriteAllText(file2, "1234567890"); // 10 bytes

        var vm = StagedSourceItemViewModel.FromFileSystem(_tempDir);

        Assert.Equal(Path.GetFileName(_tempDir), vm.Name);
        Assert.Equal(Path.GetFullPath(_tempDir), vm.FullPath);
        Assert.True(vm.IsDirectory);
        Assert.Equal(15, vm.Size);
        Assert.Equal("Icon.Folder", vm.IconKey);
        Assert.Equal("📁", vm.IconDisplay);
        Assert.Equal("15 B", vm.FormattedSize);
        Assert.True(vm.HasChildren);

        // Find child file1 and subDir
        var childFile1 = vm.Children.FirstOrDefault(c => c.Name == "file1.txt");
        var childSubDir = vm.Children.FirstOrDefault(c => c.Name == "sub_folder");

        Assert.NotNull(childFile1);
        Assert.Same(vm, childFile1.Parent);
        Assert.False(childFile1.IsDirectory);
        Assert.Equal(5, childFile1.Size);
        Assert.Equal($"{Path.GetFileName(_tempDir)}/file1.txt", childFile1.RelativePath);

        Assert.NotNull(childSubDir);
        Assert.Same(vm, childSubDir.Parent);
        Assert.True(childSubDir.IsDirectory);
        Assert.Equal(10, childSubDir.Size);
        Assert.Equal($"{Path.GetFileName(_tempDir)}/sub_folder", childSubDir.RelativePath);

        // Nested file2
        var childFile2 = childSubDir.Children.FirstOrDefault(c => c.Name == "file2.json");
        Assert.NotNull(childFile2);
        Assert.Same(childSubDir, childFile2.Parent);
        Assert.False(childFile2.IsDirectory);
        Assert.Equal(10, childFile2.Size);
        Assert.Equal("Icon.FileCode", childFile2.IconKey);
        Assert.Equal($"{Path.GetFileName(_tempDir)}/sub_folder/file2.json", childFile2.RelativePath);
    }

    [Fact]
    public void FromFileSystem_NonExistentPath_CreatesFallbackNode()
    {
        var mockPath = "/mock/path/nonexistent.png";
        var vm = StagedSourceItemViewModel.FromFileSystem(mockPath);

        Assert.Equal("nonexistent.png", vm.Name);
        Assert.Equal(mockPath, vm.FullPath);
        Assert.Equal("nonexistent.png", vm.RelativePath);
        Assert.False(vm.IsDirectory);
        Assert.Equal("Icon.FileImage", vm.IconKey);
    }

    [Fact]
    public void ExclusionCascading_ParentExcluded_CascadesToAllDescendants()
    {
        var root = new StagedSourceItemViewModel { Name = "root", IsDirectory = true };
        var child1 = new StagedSourceItemViewModel { Name = "child1", IsDirectory = true, Parent = root };
        var child2 = new StagedSourceItemViewModel { Name = "child2", IsDirectory = false, Parent = root };
        var grandChild = new StagedSourceItemViewModel { Name = "grandChild", IsDirectory = false, Parent = child1 };

        root.Children.Add(child1);
        root.Children.Add(child2);
        child1.Children.Add(grandChild);

        Assert.False(root.IsExcluded);
        Assert.False(child1.IsExcluded);
        Assert.False(child2.IsExcluded);
        Assert.False(grandChild.IsExcluded);

        // Exclude root
        root.IsExcluded = true;

        Assert.True(root.IsExcluded);
        Assert.True(child1.IsExcluded);
        Assert.True(child2.IsExcluded);
        Assert.True(grandChild.IsExcluded);
    }

    [Fact]
    public void ExclusionCascading_ChildIncluded_UnexcludesAncestors()
    {
        var root = new StagedSourceItemViewModel { Name = "root", IsDirectory = true };
        var child = new StagedSourceItemViewModel { Name = "child", IsDirectory = true, Parent = root };
        var file = new StagedSourceItemViewModel { Name = "file.txt", IsDirectory = false, Parent = child };

        root.Children.Add(child);
        child.Children.Add(file);

        // Exclude all from root
        root.IsExcluded = true;
        Assert.True(root.IsExcluded);
        Assert.True(child.IsExcluded);
        Assert.True(file.IsExcluded);

        // Include nested file
        file.IsExcluded = false;

        Assert.False(file.IsExcluded);
        Assert.False(child.IsExcluded);
        Assert.False(root.IsExcluded);
    }

    [Fact]
    public void PropertyChanged_FiresForCalculatedProperties()
    {
        var vm = new StagedSourceItemViewModel();
        var changedProps = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) changedProps.Add(e.PropertyName);
        };

        vm.Name = "script.py";
        Assert.Contains(nameof(StagedSourceItemViewModel.Name), changedProps);
        Assert.Contains(nameof(StagedSourceItemViewModel.IconKey), changedProps);
        Assert.Contains(nameof(StagedSourceItemViewModel.IconDisplay), changedProps);

        changedProps.Clear();
        vm.Size = 2048;
        Assert.Contains(nameof(StagedSourceItemViewModel.Size), changedProps);
        Assert.Contains(nameof(StagedSourceItemViewModel.FormattedSize), changedProps);
        Assert.Equal("2.0 KB", vm.FormattedSize);

        changedProps.Clear();
        vm.LastModified = DateTimeOffset.UtcNow;
        Assert.Contains(nameof(StagedSourceItemViewModel.LastModified), changedProps);
        Assert.Contains(nameof(StagedSourceItemViewModel.FormattedLastModified), changedProps);
    }
}

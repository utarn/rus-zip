using RusZip.Core.Engines;
using Xunit;

namespace RusZip.Core.Tests;

public class CompressionExclusionFilterTests
{
    [Fact]
    public void IsExcluded_NullOrEmptyFilter_ReturnsFalse()
    {
        var filterNull = new CompressionExclusionFilter(null);
        Assert.False(filterNull.HasExclusions);
        Assert.False(filterNull.IsExcluded("/path/to/file.txt", "file.txt"));

        var filterEmpty = new CompressionExclusionFilter([]);
        Assert.False(filterEmpty.HasExclusions);
        Assert.False(filterEmpty.IsExcluded("/path/to/file.txt", "file.txt"));
    }

    [Fact]
    public void IsExcluded_SingleSegmentFolder_ExcludesRootAndNestedSubtrees()
    {
        var filter = new CompressionExclusionFilter(["node_modules"]);
        Assert.True(filter.HasExclusions);

        // Root folder and children
        Assert.True(filter.IsExcluded("/repo/node_modules", "node_modules"));
        Assert.True(filter.IsExcluded("/repo/node_modules/express/index.js", "node_modules/express/index.js"));

        // Nested folder and children
        Assert.True(filter.IsExcluded("/repo/packages/client/node_modules", "packages/client/node_modules"));
        Assert.True(filter.IsExcluded("/repo/packages/client/node_modules/react/index.js", "packages/client/node_modules/react/index.js"));

        // False positive checks
        Assert.False(filter.IsExcluded("/repo/node_modules_backup/index.js", "node_modules_backup/index.js"));
        Assert.False(filter.IsExcluded("/repo/my_node_modules/file.txt", "my_node_modules/file.txt"));
        Assert.False(filter.IsExcluded("/repo/src/main.cs", "src/main.cs"));
    }

    [Fact]
    public void IsExcluded_MultiSegmentRelativePath_ExcludesMatchingPrefixAndSubtrees()
    {
        var filter = new CompressionExclusionFilter(["bin/Debug"]);

        // Exact match
        Assert.True(filter.IsExcluded("/repo/bin/Debug", "bin/Debug"));
        Assert.True(filter.IsExcluded("/repo/bin/Debug/net10.0/app.dll", "bin/Debug/net10.0/app.dll"));

        // Nested segment match
        Assert.True(filter.IsExcluded("/repo/src/RusZip.Core/bin/Debug", "src/RusZip.Core/bin/Debug"));
        Assert.True(filter.IsExcluded("/repo/src/RusZip.Core/bin/Debug/net10.0/RusZip.Core.dll", "src/RusZip.Core/bin/Debug/net10.0/RusZip.Core.dll"));

        // False positive checks
        Assert.False(filter.IsExcluded("/repo/src/RusZip.Core/bin/Debug_test/app.dll", "src/RusZip.Core/bin/Debug_test/app.dll"));
        Assert.False(filter.IsExcluded("/repo/src/RusZip.Core/my_bin/Debug/app.dll", "src/RusZip.Core/my_bin/Debug/app.dll"));
        Assert.False(filter.IsExcluded("/repo/src/RusZip.Core/bin/Release/net10.0/app.dll", "src/RusZip.Core/bin/Release/net10.0/app.dll"));
    }

    [Fact]
    public void IsExcluded_RelativeFileExclusion_MatchesExactFileAndNestedFile()
    {
        var filter = new CompressionExclusionFilter(["temp/cache.tmp"]);

        Assert.True(filter.IsExcluded("/repo/temp/cache.tmp", "temp/cache.tmp"));
        Assert.True(filter.IsExcluded("/repo/sub/temp/cache.tmp", "sub/temp/cache.tmp"));

        // Non matching
        Assert.False(filter.IsExcluded("/repo/temp/cache.tmp.bak", "temp/cache.tmp.bak"));
        Assert.False(filter.IsExcluded("/repo/temp/other.tmp", "temp/other.tmp"));
    }

    [Fact]
    public void IsExcluded_StandaloneFileName_MatchesByName()
    {
        var filter = new CompressionExclusionFilter(["secret.key"]);

        Assert.True(filter.IsExcluded("/repo/secret.key", "secret.key"));
        Assert.True(filter.IsExcluded("/repo/sub/deep/secret.key", "sub/deep/secret.key"));

        Assert.False(filter.IsExcluded("/repo/secret.key.txt", "secret.key.txt"));
        Assert.False(filter.IsExcluded("/repo/public.key", "public.key"));
    }

    [Fact]
    public void IsExcluded_AbsoluteFilesystemPath_ExcludesExactFileAndFolderSubtree()
    {
        var absFolder = Path.GetFullPath("/test/workspace/excluded_folder");
        var absFile = Path.GetFullPath("/test/workspace/secret.key");

        var filter = new CompressionExclusionFilter([absFolder, absFile]);

        // Absolute file match
        Assert.True(filter.IsExcluded(absFile, "secret.key"));
        Assert.False(filter.IsExcluded(Path.GetFullPath("/test/workspace/other.key"), "other.key"));

        // Absolute folder match
        Assert.True(filter.IsExcluded(absFolder, "excluded_folder"));
        Assert.True(filter.IsExcluded(Path.Combine(absFolder, "nested", "file.txt"), "excluded_folder/nested/file.txt"));
        Assert.False(filter.IsExcluded(Path.GetFullPath("/test/workspace/excluded_folder_sibling"), "excluded_folder_sibling"));
    }

    [Fact]
    public void IsExcluded_WithBaseDirectory_ResolvesRelativeExclusions()
    {
        var baseDir = Path.GetFullPath("/test/workspace");
        var filter = new CompressionExclusionFilter(["build/output.bin"], baseDirectory: baseDir);

        var targetFile = Path.Combine(baseDir, "build", "output.bin");
        Assert.True(filter.IsExcluded(targetFile, "build/output.bin"));
    }

    [Theory]
    [InlineData(@"bin\Debug\", "bin/Debug")]
    [InlineData("./node_modules/", "node_modules")]
    [InlineData(@"/temp/cache.tmp", "temp/cache.tmp")]
    [InlineData(@"..\a\b\", "a/b")]
    public void NormalizeRelative_NormalizesSeparatorsAndTrims(string input, string expected)
    {
        Assert.Equal(expected, CompressionExclusionFilter.NormalizeRelative(input));
    }

    [Fact]
    public void IsExcluded_CaseInsensitiveMatching()
    {
        var filter = new CompressionExclusionFilter(["Bin/DEBUG", "NODE_MODULES"]);

        Assert.True(filter.IsExcluded("/repo/bin/debug/app.dll", "bin/debug/app.dll"));
        Assert.True(filter.IsExcluded("/repo/node_modules/lodash", "node_modules/lodash"));
    }
}

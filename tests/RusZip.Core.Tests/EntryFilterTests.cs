using RusZip.Core.Engines;
using Xunit;

namespace RusZip.Core.Tests;

/// <summary>
/// Unit tests for the selective-extraction matching semantics (<see cref="EntryFilter"/>): exact
/// relative-path match OR directory-prefix match, with backslash and trailing-separator
/// normalization. A null/empty filter matches everything (extract-all behavior).
/// </summary>
public class EntryFilterTests
{
    [Fact]
    public void IsMatch_NullFilter_MatchesEverything()
    {
        IReadOnlyList<string>? entries = null;
        Assert.True(EntryFilter.IsMatch("any/path/here.txt", entries));
        Assert.True(EntryFilter.IsMatch("", entries));
        Assert.True(EntryFilter.IsMatch(null, entries));
    }

    [Fact]
    public void IsMatch_EmptyFilter_MatchesEverything()
    {
        IReadOnlyList<string> entries = new string[0];
        Assert.True(EntryFilter.IsMatch("any/path/here.txt", entries));
        Assert.True(EntryFilter.IsMatch("", entries));
        Assert.True(EntryFilter.IsMatch(null, entries));
    }

    [Fact]
    public void IsMatch_ExactFileMatch_Matches()
    {
        Assert.True(EntryFilter.IsMatch("folder/file.txt", new[] { "folder/file.txt" }));
    }

    [Fact]
    public void IsMatch_DirectoryPrefixMatch_MatchesSubtree()
    {
        // The directory entry itself.
        Assert.True(EntryFilter.IsMatch("folder", new[] { "folder" }));
        Assert.True(EntryFilter.IsMatch("folder/", new[] { "folder" }));
        // Direct children and deeper descendants.
        Assert.True(EntryFilter.IsMatch("folder/file.txt", new[] { "folder" }));
        Assert.True(EntryFilter.IsMatch("folder/sub/deep/file.txt", new[] { "folder" }));
    }

    [Fact]
    public void IsMatch_TrailingSeparatorOnFilter_IsNormalizedAway()
    {
        Assert.True(EntryFilter.IsMatch("folder/file.txt", new[] { "folder/" }));
        Assert.True(EntryFilter.IsMatch("folder", new[] { "folder/" }));
    }

    [Fact]
    public void IsMatch_BackslashPaths_AreNormalized()
    {
        Assert.True(EntryFilter.IsMatch(@"folder\file.txt", new[] { "folder/file.txt" }));
        Assert.True(EntryFilter.IsMatch("folder/file.txt", new[] { @"folder\" }));
    }

    [Fact]
    public void IsMatch_PrefixMustBePathSegmentBoundary()
    {
        // "ab" must not match "a" — the prefix rule requires a '/' separator.
        Assert.False(EntryFilter.IsMatch("ab", new[] { "a" }));
        Assert.False(EntryFilter.IsMatch("folder/file.txt", new[] { "folder/file" }));
    }

    [Fact]
    public void IsMatch_FileFilter_DoesNotMatchParentOrSiblingPaths()
    {
        Assert.False(EntryFilter.IsMatch("folder", new[] { "folder/file.txt" }));
        Assert.False(EntryFilter.IsMatch("folder/other.txt", new[] { "folder/file.txt" }));
    }

    [Fact]
    public void IsMatch_NonMatchingPaths_ReturnFalse()
    {
        Assert.False(EntryFilter.IsMatch("other.txt", new[] { "file.txt" }));
        Assert.False(EntryFilter.IsMatch("other/folder/file.txt", new[] { "folder" }));
    }

    [Fact]
    public void IsMatch_CaseSensitive()
    {
        Assert.False(EntryFilter.IsMatch("File.txt", new[] { "file.txt" }));
        Assert.False(EntryFilter.IsMatch("Folder/File.txt", new[] { "folder" }));
    }

    [Fact]
    public void IsMatch_EmptyEntryPath_ReturnsFalseWhenFiltered()
    {
        Assert.False(EntryFilter.IsMatch("", new[] { "file.txt" }));
        Assert.False(EntryFilter.IsMatch(null, new[] { "file.txt" }));
        Assert.False(EntryFilter.IsMatch("   ", new[] { "file.txt" }));
    }

    [Fact]
    public void IsMatch_EmptyFilterEntries_AreIgnored()
    {
        // A filter list containing only empty entries is effectively no-match.
        Assert.False(EntryFilter.IsMatch("file.txt", new[] { string.Empty, "   " }));
        // But a valid entry among empty ones still matches.
        Assert.True(EntryFilter.IsMatch("file.txt", new[] { string.Empty, "file.txt" }));
    }

    [Fact]
    public void IsMatch_MultipleFilterEntries_AnyCanMatch()
    {
        var filters = new[] { "a.txt", "folder" };
        Assert.True(EntryFilter.IsMatch("a.txt", filters));
        Assert.True(EntryFilter.IsMatch("folder/sub/b.txt", filters));
        Assert.False(EntryFilter.IsMatch("c.txt", filters));
    }

    [Theory]
    [InlineData("folder/", "folder")]
    [InlineData(@"nested\deep\file.txt", "nested/deep/file.txt")]
    [InlineData("/leading/trailing/", "leading/trailing")]
    [InlineData(@"a\b\", "a/b")]
    public void Normalize_ConvertsSeparatorsAndTrimsSlashes(string input, string expected)
    {
        Assert.Equal(expected, EntryFilter.Normalize(input));
    }
}

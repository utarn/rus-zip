using RusZip.Core.Models;

namespace RusZip.Core.Tests;

public sealed class EntryNameSanitizerTests
{
    [Theory]
    [InlineData("plain.txt", "plain.txt")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("folder/sub/file.txt", "folder/sub/file.txt")]
    [InlineData("C:\\Users\\test\\file.txt", "C:\\Users\\test\\file.txt")]
    [InlineData("a_b-c.txt", "a_b-c.txt")]
    public void Sanitize_NonControlText_IsUnchanged(string? input, string expected)
    {
        Assert.Equal(expected, EntryNameSanitizer.Sanitize(input));
    }

    [Fact]
    public void Sanitize_StripsEscapesAnsiSequences()
    {
        var input = "ok\u001b[31mRED\u001b[0m.txt";
        var result = EntryNameSanitizer.Sanitize(input);

        Assert.Equal("ok[31mRED[0m.txt", result);
        Assert.DoesNotContain('\u001b', result);
    }

    [Fact]
    public void Sanitize_StripsNulBytes()
    {
        var input = "a\u0000b\u0000c.txt";
        var result = EntryNameSanitizer.Sanitize(input);

        Assert.Equal("abc.txt", result);
        Assert.DoesNotContain('\u0000', result);
    }

    [Fact]
    public void Sanitize_StripsTabCrLf()
    {
        var input = "a\tb\r\nc.txt";
        var result = EntryNameSanitizer.Sanitize(input);

        Assert.Equal("abc.txt", result);
        Assert.DoesNotContain('\t', result);
        Assert.DoesNotContain('\r', result);
        Assert.DoesNotContain('\n', result);
    }

    [Fact]
    public void Sanitize_StripsAllC0ControlChars()
    {
        var input = "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000a\u000b\u000c\u000d\u000e\u000f" +
                    "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f" +
                    "value\u007f";
        var result = EntryNameSanitizer.Sanitize(input);

        Assert.Equal("value", result);
    }

    [Fact]
    public void Sanitize_StripsC1ControlChars()
    {
        var input = "a\u0080b\u0085c\u009fd";
        var result = EntryNameSanitizer.Sanitize(input);

        Assert.Equal("abcd", result);
        Assert.DoesNotContain('\u0080', result);
        Assert.DoesNotContain('\u0085', result);
        Assert.DoesNotContain('\u009f', result);
    }

    [Fact]
    public void Sanitize_PreservesNonControlUnicode()
    {
        var input = "héllo wörld/ünïcode-文件.txt";
        var result = EntryNameSanitizer.Sanitize(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void SingleLine_StripsControlsAndCollapsesNewlines()
    {
        var input = "line1\nline2\r\nline3\tends";
        var result = EntryNameSanitizer.SingleLine(input);

        Assert.Equal("line1 line2 line3ends", result);
    }

    [Fact]
    public void SingleLine_TrimsOuterWhitespace()
    {
        var input = "  error: \u001b[31mboom\u001b[0m  \n";
        var result = EntryNameSanitizer.SingleLine(input);

        Assert.Equal("error: [31mboom[0m", result);
    }

    [Fact]
    public void SingleLine_ConsecutiveNewlines_CollapseToOneSpace()
    {
        var input = "a\n\n\nb";
        var result = EntryNameSanitizer.SingleLine(input);

        Assert.Equal("a b", result);
    }

    [Fact]
    public void IsControlChar_IdentifiesControlCategories()
    {
        Assert.True(EntryNameSanitizer.IsControlChar('\u0000'));
        Assert.True(EntryNameSanitizer.IsControlChar('\u0009')); // tab
        Assert.True(EntryNameSanitizer.IsControlChar('\u001b')); // ESC
        Assert.True(EntryNameSanitizer.IsControlChar('\u007f')); // DEL
        Assert.True(EntryNameSanitizer.IsControlChar('\u0080')); // C1 start
        Assert.True(EntryNameSanitizer.IsControlChar('\u009f')); // C1 end

        Assert.False(EntryNameSanitizer.IsControlChar('a'));
        Assert.False(EntryNameSanitizer.IsControlChar(' '));
        Assert.False(EntryNameSanitizer.IsControlChar('é'));
        Assert.False(EntryNameSanitizer.IsControlChar('\u200b')); // zero-width space (Format, not Control)
    }

    [Theory]
    [InlineData("file.txt", "/base/file.txt", null, "file.txt")]
    [InlineData("sub/file.txt", "/base/sub/file.txt", null, "sub/file.txt")]
    [InlineData("sub\\file.txt", "/base/sub/file.txt", null, "sub/file.txt")]
    [InlineData("../file.txt", "/base/file.txt", null, "file.txt")]
    [InlineData("../../file.txt", "/base/file.txt", null, "file.txt")]
    [InlineData("./sub/file.txt", "/base/sub/file.txt", null, "sub/file.txt")]
    [InlineData("foo/../bar/file.txt", "/base/bar/file.txt", null, "bar/file.txt")]
    [InlineData("/tmp/absolute/file.txt", "/tmp/absolute/file.txt", null, "file.txt")]
    [InlineData("dir/", "/base/dir", null, "dir")]
    public void SanitizeRelativePath_SanitizesTraversalAndSubpaths(string raw, string full, string? baseDir, string expected)
    {
        var result = EntryNameSanitizer.SanitizeRelativePath(raw, full, baseDir);
        Assert.Equal(expected, result);
    }
}

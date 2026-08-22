using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

public class DataSizeParserTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("1024", 1024)]
    [InlineData("1KB", 1024)]
    [InlineData("1kb", 1024)]
    [InlineData("1 KiB", 1024)]
    [InlineData("1.5MB", 1572864)]
    [InlineData("10GB", 10737418240)]
    [InlineData("2TiB", 2199023255552)]
    [InlineData("512 b", 512)]
    [InlineData("  64 GB  ", 68719476736)]
    public void TryParse_ValidInput_ParsesToBytes(string input, long expected)
    {
        Assert.True(DataSizeParser.TryParse(input, out var bytes));
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("10XB")]
    [InlineData("-5")]
    [InlineData("1.2.3")]
    public void TryParse_InvalidInput_ReturnsFalse(string? input)
    {
        Assert.False(DataSizeParser.TryParse(input, out _));
    }

    [Fact]
    public void TryParse_HugeValue_ReturnsFalseWithoutOverflow()
    {
        Assert.False(DataSizeParser.TryParse("999999999999999999999999TB", out _));
    }
}

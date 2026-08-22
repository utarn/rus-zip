using RusZip.Core.Models;
using Xunit;

namespace RusZip.Core.Tests;

public class CompressionProfileTests
{
    [Theory]
    [InlineData("fast", 3)]
    [InlineData("FAST", 3)]
    [InlineData(" balanced ", 9)]
    [InlineData("high", 15)]
    [InlineData("ultra", 22)]
    [InlineData("unknown", 9)]
    [InlineData(null, 9)]
    public void ResolveLevel_ProfileNames_ReturnsExpectedLevels(string? profileName, int expectedLevel)
    {
        var result = CompressionProfiles.ResolveLevel(profileName);
        Assert.Equal(expectedLevel, result);
    }

    [Theory]
    [InlineData("fast", 5, 5)]
    [InlineData("ultra", 1, 1)]
    [InlineData(null, 22, 22)]
    public void ResolveLevel_ExplicitLevelOverridesProfile(string? profileName, int? explicitLevel, int expectedLevel)
    {
        var result = CompressionProfiles.ResolveLevel(profileName, explicitLevel);
        Assert.Equal(expectedLevel, result);
    }

    [Theory]
    [InlineData(1, CompressionProfile.Fast)]
    [InlineData(3, CompressionProfile.Fast)]
    [InlineData(5, CompressionProfile.Fast)]
    [InlineData(6, CompressionProfile.Balanced)]
    [InlineData(9, CompressionProfile.Balanced)]
    [InlineData(11, CompressionProfile.Balanced)]
    [InlineData(12, CompressionProfile.High)]
    [InlineData(15, CompressionProfile.High)]
    [InlineData(18, CompressionProfile.High)]
    [InlineData(19, CompressionProfile.Ultra)]
    [InlineData(22, CompressionProfile.Ultra)]
    public void FromLevel_MapsToExpectedProfile(int level, CompressionProfile expectedProfile)
    {
        var result = CompressionProfiles.FromLevel(level);
        Assert.Equal(expectedProfile, result);
    }

    [Fact]
    public void EnumValues_MatchStandardLevels()
    {
        Assert.Equal(3, (int)CompressionProfile.Fast);
        Assert.Equal(9, (int)CompressionProfile.Balanced);
        Assert.Equal(15, (int)CompressionProfile.High);
        Assert.Equal(22, (int)CompressionProfile.Ultra);
    }
}

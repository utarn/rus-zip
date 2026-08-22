using System.Text.Json;
using RusZip.Cli.Infrastructure;
using RusZip.Cli.Models;
using Xunit;

namespace RusZip.Cli.Tests;

public sealed class ProgressBridgeAndModelTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-100, "0 B")]
    [InlineData(512, "512.0 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(1099511627776, "1.0 TB")]
    public void FormatBytes_FormatsSizesCorrectly(long bytes, string expected)
    {
        var result = CliProgressBridge.FormatBytes(bytes);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ErrorResult_SerializesToExpectedCamelCaseJson_WithoutNullFields()
    {
        var error = new ErrorResult(false, new ErrorDetail("SOURCE_NOT_FOUND", "File not found"));
        var json = JsonSerializer.Serialize(error, CliJsonSerializer.Options);

        Assert.Contains("\"success\": false", json);
        Assert.Contains("\"code\": \"SOURCE_NOT_FOUND\"", json);
        Assert.Contains("\"message\": \"File not found\"", json);
        Assert.DoesNotContain("\"details\"", json);
    }

    [Fact]
    public void ErrorResult_WithDetails_IncludesDetailsField()
    {
        var error = new ErrorResult(false, new ErrorDetail("EXTRACT_FAILED", "Failed", "Stack trace details"));
        var json = JsonSerializer.Serialize(error, CliJsonSerializer.Options);

        Assert.Contains("\"details\": \"Stack trace details\"", json);
    }

    [Fact]
    public async Task CliProgressBridge_WhenJsonIsTrue_SuppressesProgressOutput()
    {
        bool operationCalled = false;

        await CliProgressBridge.ExecuteWithProgressAsync(
            "TestOp",
            isJson: true,
            operation: (prog, ct) =>
            {
                operationCalled = true;
                Assert.Null(prog); // Progress must be null in JSON mode
                return Task.CompletedTask;
            }
        );

        Assert.True(operationCalled);
    }
}

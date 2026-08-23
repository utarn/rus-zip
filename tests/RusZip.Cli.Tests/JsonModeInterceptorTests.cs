using RusZip.Cli.Commands.Settings;
using RusZip.Cli.Infrastructure;
using Spectre.Console.Cli;
using Xunit;

namespace RusZip.Cli.Tests;

public class JsonModeInterceptorTests
{
    private class DummySettings : CommandSettings
    {
    }

    private class TestJsonSettings : JsonCommandSettings
    {
    }

    [Fact]
    public void JsonModeInterceptor_Intercept_WithJsonSettings_SetsFlags()
    {
        JsonModeInterceptor.Reset();
        var interceptor = new JsonModeInterceptor();
        var settings = new TestJsonSettings
        {
            Json = true,
            VerboseErrors = true
        };

        interceptor.Intercept(null!, settings);

        Assert.True(JsonModeInterceptor.LastInvocationHadSettingsBound);
        Assert.True(JsonModeInterceptor.LastBoundJson);
        Assert.True(JsonModeInterceptor.LastBoundVerboseErrors);

        int result = 0;
        interceptor.InterceptResult(null!, settings, ref result);

        JsonModeInterceptor.Reset();
        Assert.False(JsonModeInterceptor.LastInvocationHadSettingsBound);
        Assert.False(JsonModeInterceptor.LastBoundJson);
        Assert.False(JsonModeInterceptor.LastBoundVerboseErrors);
    }

    [Fact]
    public void JsonModeInterceptor_Intercept_WithNonJsonSettings_SetsFlagsFalse()
    {
        JsonModeInterceptor.Reset();
        var interceptor = new JsonModeInterceptor();
        var settings = new DummySettings();

        interceptor.Intercept(null!, settings);

        Assert.True(JsonModeInterceptor.LastInvocationHadSettingsBound);
        Assert.False(JsonModeInterceptor.LastBoundJson);
        Assert.False(JsonModeInterceptor.LastBoundVerboseErrors);

        JsonModeInterceptor.Reset();
    }
}

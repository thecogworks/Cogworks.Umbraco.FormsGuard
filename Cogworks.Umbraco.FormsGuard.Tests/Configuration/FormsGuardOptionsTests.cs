using Cogworks.Umbraco.FormsGuard.Configuration;

namespace Cogworks.Umbraco.FormsGuard.Tests.Configuration;

[Collection(EnvironmentCollection.Name)]
public class FormsGuardOptionsTests
{
    [Fact]
    public void SectionName_IsCogworksFormsGuard()
    {
        Assert.Equal("Cogworks:FormsGuard", FormsGuardOptions.SectionName);
    }

    [Fact]
    public void Defaults_AreEnabledWithProcessorDefaults()
    {
        var options = new FormsGuardOptions();

        Assert.True(options.Enabled);
        Assert.Equal(10, options.ProcessorIntervalSeconds);
        Assert.Equal(10, options.ProcessorBatchSize);
        Assert.Equal(4, options.ProcessorMaxConcurrency);
        Assert.Equal(300, options.ClaimStaleSeconds);
        Assert.Equal(30, options.RetryBaseSeconds);
        Assert.Equal(1800, options.RetryMaxSeconds);
        Assert.Equal(5, options.MaxAttempts);
        Assert.False(options.KillSwitch);
        Assert.Equal(30, options.QuarantineRetentionDays);
        Assert.Equal(StubVerdict.Approve, options.StubVerdict);
    }

    [Fact]
    public void Defaults_UseJevPinnedModel()
    {
        var options = new FormsGuardOptions();

        Assert.Equal("jev", options.Provider);
        Assert.Null(options.Jev.ApiKey);
        Assert.Equal("https://api.typesafe.ai", options.Jev.BaseUrl);
        Assert.Equal("jev-1.13.0", options.Jev.Model);
        Assert.Equal(10, options.Jev.TimeoutSeconds);
    }

    [Fact]
    public void ResolveApiKey_PrefersConfiguredKey()
    {
        var jev = new JevOptions { ApiKey = " configured " };

        Assert.Equal("configured", jev.ResolveApiKey());
    }

    [Fact]
    public void ResolveApiKey_BlankFallsBackToEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable(JevOptions.ApiKeyEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(JevOptions.ApiKeyEnvironmentVariable, "from-env");
            Assert.Equal("from-env", new JevOptions { ApiKey = "  " }.ResolveApiKey());

            Environment.SetEnvironmentVariable(JevOptions.ApiKeyEnvironmentVariable, null);
            Assert.Null(new JevOptions { ApiKey = "" }.ResolveApiKey());
        }
        finally
        {
            Environment.SetEnvironmentVariable(JevOptions.ApiKeyEnvironmentVariable, previous);
        }
    }
}

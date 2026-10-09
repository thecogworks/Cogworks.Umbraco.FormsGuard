using Cogworks.Umbraco.FormsGuard.Decisions;

namespace Cogworks.Umbraco.FormsGuard.Tests.Decisions;

public class DecisionProvidersTests
{
    private sealed class Named(string alias) : IDecisionProvider
    {
        public string Alias => alias;

        public Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(DecisionResult.Succeeded([], alias));
    }

    private static readonly IDecisionProvider[] Providers = [new Named("stub"), new Named("jev"), new Named("umbracoai")];

    [Fact]
    public void Select_Match()
    {
        Assert.Same(Providers[1], DecisionProviders.Select(Providers, "jev"));
    }

    [Theory]
    [InlineData("UmbracoAI")]
    [InlineData("UMBRACOAI")]
    [InlineData(" umbracoai ")]
    public void Select_IgnoresCase(string alias)
    {
        Assert.Same(Providers[2], DecisionProviders.Select(Providers, alias));
    }

    [Fact]
    public async Task Select_Unknown_AlwaysFailsNonRetryable()
    {
        var provider = DecisionProviders.Select(Providers, "foo");

        Assert.IsType<UnregisteredDecisionProvider>(provider);
        Assert.Equal("foo", provider.Alias);
        var result = await provider.DecideAsync(
            new DecisionRequest(Guid.NewGuid(), new DecisionState("o", "f", null, new Dictionary<string, string>()), []),
            CancellationToken.None);
        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Contains("'foo'", result.Error);
    }

    [Fact]
    public void Select_NoProviders_ReturnsUnregistered()
    {
        Assert.IsType<UnregisteredDecisionProvider>(DecisionProviders.Select([], "umbracoai"));
    }
}

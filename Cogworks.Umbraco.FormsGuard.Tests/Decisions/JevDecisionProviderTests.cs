using System.Net;
using System.Text;
using Cogworks.Umbraco.FormsGuard.Configuration;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Decisions.Jev;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cogworks.Umbraco.FormsGuard.Tests.Decisions;

[Collection(EnvironmentCollection.Name)]
public class JevDecisionProviderTests
{
    private static readonly DecisionRequest Request = new(
        Guid.NewGuid(),
        new DecisionState("Cogworks", "Contact Us", null, new Dictionary<string, string> { ["Message"] = "Hi" }),
        new[] { new DecisionQuestion("guard.genuine", "Genuine?", QuestionRole.GenuineSignal) });

    [Fact]
    public async Task DecideAsync_SendsBearerToSystemOne()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"model":"jev-1.13.0","answers":{"guard.genuine":{"type":"noul","noul":0.9}}}""", Encoding.UTF8, "application/json"),
        }));
        var provider = Create(handler, new JevOptions { ApiKey = "secret", BaseUrl = "https://jev.test/" });

        var result = await provider.DecideAsync(Request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("jev-1.13.0", result.ModelVersion);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://jev.test/v1/systemone", sent.RequestUri!.ToString());
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.Equal("secret", sent.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task DecideAsync_Timeout_Retryable()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provider = Create(handler, new JevOptions { ApiKey = "secret", TimeoutSeconds = 1 });

        var result = await provider.DecideAsync(Request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.Retryable);
    }

    [Fact]
    public async Task DecideAsync_HttpRequestException_Retryable()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("connection refused"));
        var provider = Create(handler, new JevOptions { ApiKey = "secret" });

        var result = await provider.DecideAsync(Request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.Retryable);
    }

    [Fact]
    public async Task DecideAsync_ErrorStatus_DoesNotLeakBody()
    {
        var handler = new FakeHandler((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)422) { Content = new StringContent("secret body detail") };
            response.Headers.Add("x-typesafe-request-id", "req-9");
            return Task.FromResult(response);
        });
        var provider = Create(handler, new JevOptions { ApiKey = "secret" });

        var result = await provider.DecideAsync(Request, CancellationToken.None);

        Assert.False(result.Retryable);
        Assert.Contains("req-9", result.Error);
        Assert.DoesNotContain("secret", result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("api.typesafe.ai")]
    [InlineData("/var/jev")]
    public async Task DecideAsync_InvalidBaseUrl_NotRetryable_NoCall(string? baseUrl)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var provider = Create(handler, new JevOptions { ApiKey = "secret", BaseUrl = baseUrl! });

        var result = await provider.DecideAsync(Request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Equal("Jev BaseUrl is not a valid absolute URL", result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DecideAsync_NoKey_NoCall()
    {
        var previous = Environment.GetEnvironmentVariable(JevOptions.ApiKeyEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(JevOptions.ApiKeyEnvironmentVariable, null);
            var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            var provider = Create(handler, new JevOptions { ApiKey = "" });

            var result = await provider.DecideAsync(Request, CancellationToken.None);

            Assert.False(result.Success);
            Assert.False(result.Retryable);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Environment.SetEnvironmentVariable(JevOptions.ApiKeyEnvironmentVariable, previous);
        }
    }

    private static JevDecisionProvider Create(FakeHandler handler, JevOptions jev) =>
        new(new HttpClient(handler), new StaticOptions(new FormsGuardOptions { Jev = jev }), NullLogger<JevDecisionProvider>.Instance);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return _respond(request, cancellationToken);
        }
    }

    private sealed class StaticOptions : IOptionsMonitor<FormsGuardOptions>
    {
        public StaticOptions(FormsGuardOptions value) => CurrentValue = value;

        public FormsGuardOptions CurrentValue { get; }

        public FormsGuardOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<FormsGuardOptions, string?> listener) => null;
    }
}

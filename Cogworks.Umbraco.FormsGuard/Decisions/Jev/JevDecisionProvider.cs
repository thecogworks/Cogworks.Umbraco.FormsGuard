using System.Net.Http.Headers;
using System.Text;
using Cogworks.Umbraco.FormsGuard.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cogworks.Umbraco.FormsGuard.Decisions.Jev;

/// <summary>
/// Calls Jev (TypeSafe AI) at <c>POST {BaseUrl}/v1/systemone</c>. One attempt per call: retries and
/// backoff belong to the caller. Never throws; every failure is <see cref="DecisionResult.Failed"/>.
/// </summary>
public sealed class JevDecisionProvider : IDecisionProvider
{
    public const string ProviderAlias = "jev";
    private const string RequestIdHeader = "x-typesafe-request-id";

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<FormsGuardOptions> _options;
    private readonly ILogger<JevDecisionProvider> _logger;

    public JevDecisionProvider(
        HttpClient httpClient,
        IOptionsMonitor<FormsGuardOptions> options,
        ILogger<JevDecisionProvider> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public string Alias => ProviderAlias;

    public async Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var jev = _options.CurrentValue.Jev;
            var apiKey = jev.ResolveApiKey();
            if (apiKey is null)
            {
                _logger.LogWarning("FormsGuard: Jev API key is not configured; form {FormId} not decided", request.FormId);
                return DecisionResult.Failed("Jev API key is not configured", retryable: false);
            }

            if (!Uri.TryCreate((jev.BaseUrl ?? string.Empty).TrimEnd('/') + "/v1/systemone", UriKind.Absolute, out var endpoint)
                || string.IsNullOrWhiteSpace(jev.BaseUrl)
                || (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
            {
                _logger.LogWarning("FormsGuard: Jev BaseUrl is not a valid absolute URL; form {FormId} not decided", request.FormId);
                return DecisionResult.Failed("Jev BaseUrl is not a valid absolute URL", retryable: false);
            }

            var body = JevMapper.BuildBody(request, jev.Model).ToJsonString();
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(jev.TimeoutSeconds, 1, 120)));

            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var requestId = response.Headers.TryGetValues(RequestIdHeader, out var ids) ? ids.FirstOrDefault() : null;
                _logger.LogWarning(
                    "FormsGuard: Jev returned HTTP {StatusCode} for form {FormId}", (int)response.StatusCode, request.FormId);
                return JevMapper.Classify(response.StatusCode, response.Headers.RetryAfter, requestId);
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            var result = JevMapper.Parse(json, request.Questions);
            if (!result.Success)
            {
                _logger.LogWarning("FormsGuard: Jev response for form {FormId} was unusable", request.FormId);
            }

            return result;
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning("FormsGuard: Jev call timed out or was cancelled for form {FormId}", request.FormId);
            return DecisionResult.Failed("Jev request timed out", retryable: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("FormsGuard: Jev network error for form {FormId} ({Error})", request.FormId, ex.HttpRequestError);
            return DecisionResult.Failed("Jev network error", retryable: true);
        }
        catch (Exception ex)
        {
            // Contract: never throw. Log the type only, never the message (it may echo request content).
            _logger.LogError("FormsGuard: unexpected {ExceptionType} calling Jev for form {FormId}", ex.GetType().Name, request.FormId);
            return DecisionResult.Failed("Jev call failed unexpectedly", retryable: true);
        }
    }
}

using Cogworks.Umbraco.FormsGuard.Decisions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.AI.Core.Chat;

namespace Cogworks.Umbraco.FormsGuard.UmbracoAI;

/// <summary>
/// Answers every question in one <see cref="IAIChatService"/> call using the configured Umbraco.AI profile.
/// One attempt per call: retries belong to the processor. Never throws; every failure is
/// <see cref="DecisionResult.Failed"/>. Logs never contain field values or model output.
/// </summary>
public sealed class UmbracoAIDecisionProvider : IDecisionProvider
{
    public const string ProviderAlias = "umbracoai";
    private const string ChatAlias = "forms-guard";

    private readonly IAIChatService _chat;
    private readonly IOptionsMonitor<UmbracoAIDecisionOptions> _options;
    private readonly ILogger<UmbracoAIDecisionProvider> _logger;

    public UmbracoAIDecisionProvider(
        IAIChatService chat,
        IOptionsMonitor<UmbracoAIDecisionOptions> options,
        ILogger<UmbracoAIDecisionProvider> logger)
    {
        _chat = chat;
        _options = options;
        _logger = logger;
    }

    public string Alias => ProviderAlias;

    public async Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var options = _options.CurrentValue;
            var prompt = UmbracoAIMapper.BuildPrompt(request);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 120)));

            var response = await _chat.GetChatResponseAsync(
                b => b.WithAlias(ChatAlias).WithProfile(options.ProfileAlias),
                [new ChatMessage(ChatRole.User, prompt)],
                timeout.Token);

            var result = UmbracoAIMapper.Parse(
                response.Text,
                request.Questions,
                response.ModelId,
                response.Usage?.InputTokenCount,
                response.Usage?.OutputTokenCount);
            if (!result.Success)
            {
                _logger.LogWarning("FormsGuard: Umbraco.AI reply for form {FormId} was unusable ({Error})", request.FormId, result.Error);
            }

            return result;
        }
        catch (Exception ex)
        {
            // Contract: never throw. Log the type only, never the message (it may echo request content).
            var result = UmbracoAIMapper.Classify(ex);
            _logger.LogWarning(
                "FormsGuard: Umbraco.AI call failed for form {FormId} with {ExceptionType} ({Error}; retryable: {Retryable})",
                request.FormId, ex.GetType().Name, result.Error, result.Retryable);
            return result;
        }
    }
}

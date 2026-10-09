using Cogworks.Umbraco.FormsGuard.Configuration;
using Microsoft.Extensions.Options;

namespace Cogworks.Umbraco.FormsGuard.Decisions;

/// <summary>
/// Placeholder provider. When <see cref="FormsGuardOptions.StubVerdict"/> is <see cref="StubVerdict.Reject"/>,
/// answers every question with 1.0; otherwise answers genuine-signal questions with 1.0 and every other
/// question with 0.0, so the decision rule approves.
/// </summary>
public sealed class StubDecisionProvider : IDecisionProvider
{
    private readonly IOptionsMonitor<FormsGuardOptions> _options;

    public StubDecisionProvider(IOptionsMonitor<FormsGuardOptions> options) => _options = options;

    public string Alias => "stub";

    public Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken)
    {
        var reject = _options.CurrentValue.StubVerdict == StubVerdict.Reject;
        var answers = request.Questions
            .Select(q => new DecisionAnswer(q.Key, reject || q.Role == QuestionRole.GenuineSignal ? 1.0 : 0.0))
            .ToList();
        return Task.FromResult(DecisionResult.Succeeded(answers, "stub"));
    }
}

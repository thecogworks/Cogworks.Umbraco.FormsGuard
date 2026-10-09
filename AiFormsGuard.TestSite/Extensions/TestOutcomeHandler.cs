using Cogworks.Umbraco.FormsGuard.Decisions;

namespace AiFormsGuard.TestSite.Extensions;

/// <summary>Sample handler: logs the outcome and the <c>test.deadline</c> probability.</summary>
public sealed class TestOutcomeHandler : IOutcomeHandler
{
    private readonly ILogger<TestOutcomeHandler> _logger;

    public TestOutcomeHandler(ILogger<TestOutcomeHandler> logger) => _logger = logger;

    public Task HandleAsync(DecisionOutcome outcome, CancellationToken cancellationToken)
    {
        var deadline = outcome.Answers.FirstOrDefault(a => a.Key == "test.deadline")?.Probability;
        _logger.LogInformation(
            "TestOutcomeHandler: record {RecordId} decided {Status}; test.deadline {Deadline}",
            outcome.RecordId, outcome.Status, deadline);
        return Task.CompletedTask;
    }
}

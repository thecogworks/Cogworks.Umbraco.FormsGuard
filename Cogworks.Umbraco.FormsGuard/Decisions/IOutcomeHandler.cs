namespace Cogworks.Umbraco.FormsGuard.Decisions;

/// <summary>The decided outcome for one Forms record, passed to outcome handlers.</summary>
public sealed record DecisionOutcome(
    Guid RecordId,
    Guid FormId,
    DecisionStatus Status,
    IReadOnlyList<DecisionAnswer> Answers);

/// <summary>
/// Acts on a decision. Runs after the core outcome (approve, quarantine or review) has been applied.
/// </summary>
public interface IOutcomeHandler
{
    Task HandleAsync(DecisionOutcome outcome, CancellationToken cancellationToken);
}

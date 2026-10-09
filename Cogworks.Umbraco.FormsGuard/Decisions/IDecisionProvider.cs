namespace Cogworks.Umbraco.FormsGuard.Decisions;

/// <summary>
/// Answers typed questions about an entry's state. Implementations must not throw for
/// provider failures (timeouts, HTTP errors, bad responses); return
/// <see cref="DecisionResult.Failed"/> instead.
/// </summary>
public interface IDecisionProvider
{
    /// <summary>Unique alias used to select this provider in configuration.</summary>
    string Alias { get; }

    /// <summary>
    /// Answers every question in the request in one call. Failures are returned as
    /// <see cref="DecisionResult.Failed"/>, never thrown.
    /// </summary>
    Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken);
}

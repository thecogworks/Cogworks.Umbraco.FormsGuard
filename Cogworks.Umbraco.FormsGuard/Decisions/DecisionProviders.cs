namespace Cogworks.Umbraco.FormsGuard.Decisions;

/// <summary>Selects the configured decision provider from every registered <see cref="IDecisionProvider"/>.</summary>
public static class DecisionProviders
{
    /// <summary>
    /// Returns the provider whose <see cref="IDecisionProvider.Alias"/> matches <paramref name="alias"/>, ignoring case,
    /// or an <see cref="UnregisteredDecisionProvider"/> that always fails non-retryably so the failure policy applies.
    /// </summary>
    public static IDecisionProvider Select(IEnumerable<IDecisionProvider> providers, string? alias) =>
        providers.FirstOrDefault(p => string.Equals(p.Alias, alias?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? new UnregisteredDecisionProvider(alias ?? string.Empty);
}

/// <summary>Stands in when no registered provider matches the configured alias. Never throws.</summary>
public sealed class UnregisteredDecisionProvider : IDecisionProvider
{
    public UnregisteredDecisionProvider(string alias) => Alias = alias;

    public string Alias { get; }

    public Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(DecisionResult.Failed(
            $"No decision provider is registered with alias '{Alias}'; check Cogworks:FormsGuard:Provider and the installed provider packages",
            retryable: false));
}

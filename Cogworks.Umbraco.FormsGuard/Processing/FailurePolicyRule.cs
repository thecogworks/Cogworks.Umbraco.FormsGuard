using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Settings;

namespace Cogworks.Umbraco.FormsGuard.Processing;

/// <summary>When the form's failure policy applies, and what it decides.</summary>
public static class FailurePolicyRule
{
    /// <summary>True when the <paramref name="failures"/>th failure (counted from 1) reaches <paramref name="maxAttempts"/>.</summary>
    public static bool CapReached(int failures, int maxAttempts) => failures >= Math.Max(1, maxAttempts);

    /// <summary>
    /// True when the failure policy decides instead of the provider: the kill switch is on, or the
    /// <paramref name="failures"/>th failure reaches the cap. Pass 0 failures before any provider call.
    /// </summary>
    public static bool Applies(bool killSwitch, int failures, int maxAttempts) =>
        killSwitch || (failures > 0 && CapReached(failures, maxAttempts));

    /// <summary>The outcome the policy gives: approved but not checked, or left for review.</summary>
    public static DecisionStatus Outcome(FailurePolicy policy) =>
        policy == FailurePolicy.Review ? DecisionStatus.Review : DecisionStatus.ApprovedNotChecked;
}

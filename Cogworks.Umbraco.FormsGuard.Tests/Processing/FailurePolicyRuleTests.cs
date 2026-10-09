using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Processing;
using Cogworks.Umbraco.FormsGuard.Settings;

namespace Cogworks.Umbraco.FormsGuard.Tests.Processing;

public class FailurePolicyRuleTests
{
    [Theory]
    [InlineData(1, 5, false)]
    [InlineData(4, 5, false)]
    [InlineData(5, 5, true)]
    [InlineData(6, 5, true)]
    [InlineData(1, 1, true)]
    [InlineData(1, 0, true)]
    public void CapReached_AtOrAfterMaxAttempts(int failures, int maxAttempts, bool expected)
    {
        Assert.Equal(expected, FailurePolicyRule.CapReached(failures, maxAttempts));
    }

    [Theory]
    [InlineData(true, 0, 5, true)]
    [InlineData(true, 1, 5, true)]
    [InlineData(false, 0, 5, false)]
    [InlineData(false, 0, 1, false)]
    [InlineData(false, 4, 5, false)]
    [InlineData(false, 5, 5, true)]
    public void Applies_WhenKillSwitchOnOrCapReached(bool killSwitch, int failures, int maxAttempts, bool expected)
    {
        Assert.Equal(expected, FailurePolicyRule.Applies(killSwitch, failures, maxAttempts));
    }

    [Theory]
    [InlineData(FailurePolicy.ApproveNotChecked, DecisionStatus.ApprovedNotChecked)]
    [InlineData(FailurePolicy.Review, DecisionStatus.Review)]
    public void Outcome_FollowsPolicy(FailurePolicy policy, DecisionStatus expected)
    {
        Assert.Equal(expected, FailurePolicyRule.Outcome(policy));
    }
}

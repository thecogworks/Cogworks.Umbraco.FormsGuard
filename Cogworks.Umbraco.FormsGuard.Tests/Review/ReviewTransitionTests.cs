using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Review;
using Umbraco.Forms.Core.Enums;

namespace Cogworks.Umbraco.FormsGuard.Tests.Review;

public class ReviewTransitionTests
{
    [Theory]
    [InlineData(ReviewAction.Approve, DecisionStatus.Pending, null)]
    [InlineData(ReviewAction.Approve, DecisionStatus.Approved, null)]
    [InlineData(ReviewAction.Approve, DecisionStatus.ApprovedNotChecked, null)]
    [InlineData(ReviewAction.Approve, DecisionStatus.Quarantined, null)]
    [InlineData(ReviewAction.Approve, DecisionStatus.Review, DecisionStatus.Approved)]
    [InlineData(ReviewAction.ConfirmSpam, DecisionStatus.Pending, null)]
    [InlineData(ReviewAction.ConfirmSpam, DecisionStatus.Approved, null)]
    [InlineData(ReviewAction.ConfirmSpam, DecisionStatus.ApprovedNotChecked, null)]
    [InlineData(ReviewAction.ConfirmSpam, DecisionStatus.Quarantined, null)]
    [InlineData(ReviewAction.ConfirmSpam, DecisionStatus.Review, DecisionStatus.Quarantined)]
    [InlineData(ReviewAction.Restore, DecisionStatus.Pending, null)]
    [InlineData(ReviewAction.Restore, DecisionStatus.Approved, null)]
    [InlineData(ReviewAction.Restore, DecisionStatus.ApprovedNotChecked, null)]
    [InlineData(ReviewAction.Restore, DecisionStatus.Quarantined, DecisionStatus.Approved)]
    [InlineData(ReviewAction.Restore, DecisionStatus.Review, null)]
    public void Target_AllowsOnlyTheThreeTransitions(ReviewAction action, DecisionStatus from, DecisionStatus? expected) =>
        Assert.Equal(expected, ReviewTransition.Target(action, from));

    [Fact]
    public void Target_CoversEveryActionAndStatus()
    {
        // Guards the theory above against new enum members slipping through untested.
        Assert.Equal(3, Enum.GetValues<ReviewAction>().Length);
        Assert.Equal(5, Enum.GetValues<DecisionStatus>().Length);
    }

    [Theory]
    [InlineData(DecisionStatus.Approved, FormState.Submitted, true)]
    [InlineData(DecisionStatus.Approved, FormState.Approved, false)]
    [InlineData(DecisionStatus.Approved, FormState.Rejected, true)]
    [InlineData(DecisionStatus.ApprovedNotChecked, FormState.Submitted, true)]
    [InlineData(DecisionStatus.ApprovedNotChecked, FormState.Approved, false)]
    [InlineData(DecisionStatus.ApprovedNotChecked, FormState.Rejected, true)]
    [InlineData(DecisionStatus.Quarantined, FormState.Submitted, true)]
    [InlineData(DecisionStatus.Quarantined, FormState.Approved, true)]
    [InlineData(DecisionStatus.Quarantined, FormState.Rejected, false)]
    [InlineData(DecisionStatus.Review, FormState.Submitted, false)]
    [InlineData(DecisionStatus.Review, FormState.Approved, false)]
    [InlineData(DecisionStatus.Review, FormState.Rejected, false)]
    [InlineData(DecisionStatus.Pending, FormState.Submitted, false)]
    [InlineData(DecisionStatus.Pending, FormState.Approved, false)]
    [InlineData(DecisionStatus.Pending, FormState.Rejected, false)]
    public void NeedsRecordCall_SkipsWhenTheRecordIsAlreadyThere(DecisionStatus target, FormState current, bool expected) =>
        Assert.Equal(expected, ReviewTransition.NeedsRecordCall(target, current));

    [Theory]
    [InlineData(ReviewAction.Approve, "approve")]
    [InlineData(ReviewAction.ConfirmSpam, "confirm-spam")]
    [InlineData(ReviewAction.Restore, "restore")]
    public void AuditAction_Names(ReviewAction action, string expected) =>
        Assert.Equal(expected, ReviewTransition.AuditAction(action));
}

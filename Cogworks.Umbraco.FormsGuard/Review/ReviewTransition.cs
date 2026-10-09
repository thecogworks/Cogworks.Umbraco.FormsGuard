using Cogworks.Umbraco.FormsGuard.Decisions;
using Umbraco.Forms.Core.Enums;

namespace Cogworks.Umbraco.FormsGuard.Review;

/// <summary>A reviewer's action on a decision.</summary>
public enum ReviewAction
{
    /// <summary>Review to Approved.</summary>
    Approve,

    /// <summary>Review to Quarantined.</summary>
    ConfirmSpam,

    /// <summary>Quarantined to Approved.</summary>
    Restore,
}

/// <summary>The allowed review transitions as plain functions of values, so they can be tested.</summary>
public static class ReviewTransition
{
    /// <summary>The status <paramref name="action"/> moves a row to from <paramref name="from"/>, or null when it is refused.</summary>
    public static DecisionStatus? Target(ReviewAction action, DecisionStatus from) => (action, from) switch
    {
        (ReviewAction.Approve, DecisionStatus.Review) => DecisionStatus.Approved,
        (ReviewAction.ConfirmSpam, DecisionStatus.Review) => DecisionStatus.Quarantined,
        (ReviewAction.Restore, DecisionStatus.Quarantined) => DecisionStatus.Approved,
        _ => null,
    };

    /// <summary>
    /// True when the Forms record must be approved or rejected to reach <paramref name="target"/>; false when it is
    /// already there (so workflows never run twice) or the target needs no record change.
    /// </summary>
    public static bool NeedsRecordCall(DecisionStatus target, FormState current) => target switch
    {
        DecisionStatus.Approved or DecisionStatus.ApprovedNotChecked => current != FormState.Approved,
        DecisionStatus.Quarantined => current != FormState.Rejected,
        _ => false,
    };

    /// <summary>The audit action name for <paramref name="action"/>.</summary>
    public static string AuditAction(ReviewAction action) => action switch
    {
        ReviewAction.Approve => "approve",
        ReviewAction.ConfirmSpam => "confirm-spam",
        ReviewAction.Restore => "restore",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };
}

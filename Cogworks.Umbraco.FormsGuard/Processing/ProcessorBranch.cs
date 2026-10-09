using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Umbraco.Forms.Core.Enums;

namespace Cogworks.Umbraco.FormsGuard.Processing;

/// <summary>What the processor does with a claimed row, in branch order.</summary>
public enum ProcessorStep
{
    /// <summary>The form or record no longer exists; delete the row.</summary>
    DeleteMissing,

    /// <summary>The record is already Approved or Rejected in Forms; complete the row from that state.</summary>
    CompleteFromRecord,

    /// <summary>The record is in some other non-Submitted state; delete the row.</summary>
    DeleteOtherState,

    /// <summary>The form is no longer guarded; delete the row and leave the record alone.</summary>
    SkipUnguarded,

    /// <summary>A hard rule decided.</summary>
    Rule,

    /// <summary>No questions to ask; leave for Review.</summary>
    NoQuestions,

    /// <summary>The kill switch is on; the failure policy decides without a provider call.</summary>
    KillSwitch,

    /// <summary>Ask the provider.</summary>
    CallProvider,
}

/// <summary>What the processor does after a failed provider call.</summary>
public enum ProviderFailureStep
{
    /// <summary>The form's failure policy decides now.</summary>
    ApplyPolicy,

    /// <summary>Release the row for another attempt on the backoff schedule.</summary>
    Release,
}

/// <summary>The processor's branch choice as plain functions of values, so the order can be tested.</summary>
public static class ProcessorBranch
{
    /// <summary>Picks the processor's step. A null <paramref name="recordState"/> means the form or record is missing.</summary>
    public static ProcessorStep Choose(FormState? recordState, bool guarded, bool ruleHit, int questionCount, bool killSwitch)
    {
        if (recordState is not { } state)
        {
            return ProcessorStep.DeleteMissing;
        }

        if (FromRecordState(state) is not null)
        {
            return ProcessorStep.CompleteFromRecord;
        }

        if (state != FormState.Submitted)
        {
            return ProcessorStep.DeleteOtherState;
        }

        if (!guarded)
        {
            return ProcessorStep.SkipUnguarded;
        }

        if (ruleHit)
        {
            return ProcessorStep.Rule;
        }

        if (questionCount == 0)
        {
            return ProcessorStep.NoQuestions;
        }

        return killSwitch ? ProcessorStep.KillSwitch : ProcessorStep.CallProvider;
    }

    /// <summary>
    /// After the <paramref name="failures"/>th failed provider call (counted from 1): a non-retryable failure, the kill
    /// switch or reaching the cap applies the policy, otherwise the row is released.
    /// </summary>
    public static ProviderFailureStep OnProviderFailure(bool retryable, bool killSwitch, int failures, int maxAttempts) =>
        !retryable || FailurePolicyRule.Applies(killSwitch, failures, maxAttempts)
            ? ProviderFailureStep.ApplyPolicy
            : ProviderFailureStep.Release;

    /// <summary>
    /// The audit action and detail for a row the processor deletes at <paramref name="step"/>: <c>DeleteMissing</c>,
    /// <c>DeleteOtherState</c> (with the record's <paramref name="state"/>) or <c>SkipUnguarded</c>.
    /// </summary>
    public static (string Action, string Detail) RemovalAudit(ProcessorStep step, FormState? state) => step switch
    {
        ProcessorStep.DeleteMissing => (DecisionRepository.RecordDeletedAction, OrphanSweep.Detail),
        ProcessorStep.DeleteOtherState => (DecisionRepository.DecisionRemovedAction, $"record already {state}"),
        ProcessorStep.SkipUnguarded => (DecisionRepository.DecisionRemovedAction, "form not guarded"),
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Step does not delete the decision row."),
    };

    /// <summary>The decision status a Forms record state stands for: Approved or Quarantined, otherwise null.</summary>
    public static DecisionStatus? FromRecordState(FormState state) => state switch
    {
        FormState.Approved => DecisionStatus.Approved,
        FormState.Rejected => DecisionStatus.Quarantined,
        _ => null,
    };
}

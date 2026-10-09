using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Processing;
using Umbraco.Forms.Core.Enums;

namespace Cogworks.Umbraco.FormsGuard.Tests.Processing;

public class ProcessorBranchTests
{
    // Every later condition set to the value that would trigger its own branch, so the earlier branch must win.
    private const bool Unguarded = false;
    private const bool RuleHit = true;
    private const int NoQuestions = 0;
    private const bool KillSwitchOn = true;

    [Fact]
    public void MissingRecord_WinsOverEverything()
    {
        Assert.Equal(ProcessorStep.DeleteMissing, ProcessorBranch.Choose(null, Unguarded, RuleHit, NoQuestions, KillSwitchOn));
        Assert.Equal(ProcessorStep.DeleteMissing, ProcessorBranch.Choose(null, true, false, 3, false));
    }

    [Theory]
    [InlineData(FormState.Approved)]
    [InlineData(FormState.Rejected)]
    public void RecordAlreadyDecided_WinsOverLaterBranches(FormState state)
    {
        Assert.Equal(ProcessorStep.CompleteFromRecord, ProcessorBranch.Choose(state, Unguarded, RuleHit, NoQuestions, KillSwitchOn));
        Assert.Equal(ProcessorStep.CompleteFromRecord, ProcessorBranch.Choose(state, true, false, 3, false));
    }

    [Theory]
    [InlineData(FormState.Opened)]
    [InlineData(FormState.Deleted)]
    [InlineData(FormState.PartiallySubmitted)]
    public void OtherState_WinsOverLaterBranches(FormState state)
    {
        Assert.Equal(ProcessorStep.DeleteOtherState, ProcessorBranch.Choose(state, Unguarded, RuleHit, NoQuestions, KillSwitchOn));
        Assert.Equal(ProcessorStep.DeleteOtherState, ProcessorBranch.Choose(state, true, false, 3, false));
    }

    [Fact]
    public void Unguarded_WinsOverRuleQuestionsAndKillSwitch()
    {
        Assert.Equal(ProcessorStep.SkipUnguarded, ProcessorBranch.Choose(FormState.Submitted, Unguarded, RuleHit, NoQuestions, KillSwitchOn));
        Assert.Equal(ProcessorStep.SkipUnguarded, ProcessorBranch.Choose(FormState.Submitted, Unguarded, false, 3, false));
    }

    [Fact]
    public void Rule_WinsOverQuestionsAndKillSwitch()
    {
        Assert.Equal(ProcessorStep.Rule, ProcessorBranch.Choose(FormState.Submitted, true, RuleHit, NoQuestions, KillSwitchOn));
        Assert.Equal(ProcessorStep.Rule, ProcessorBranch.Choose(FormState.Submitted, true, RuleHit, 3, false));
    }

    [Fact]
    public void NoQuestions_WinsOverKillSwitch()
    {
        Assert.Equal(ProcessorStep.NoQuestions, ProcessorBranch.Choose(FormState.Submitted, true, false, NoQuestions, KillSwitchOn));
        Assert.Equal(ProcessorStep.NoQuestions, ProcessorBranch.Choose(FormState.Submitted, true, false, NoQuestions, false));
    }

    [Fact]
    public void KillSwitch_WinsOverProvider()
    {
        Assert.Equal(ProcessorStep.KillSwitch, ProcessorBranch.Choose(FormState.Submitted, true, false, 3, KillSwitchOn));
    }

    [Fact]
    public void OtherwiseCallsProvider()
    {
        Assert.Equal(ProcessorStep.CallProvider, ProcessorBranch.Choose(FormState.Submitted, true, false, 3, false));
    }

    [Theory]
    [InlineData(false, false, 1, 5, ProviderFailureStep.ApplyPolicy)]
    [InlineData(false, false, 4, 5, ProviderFailureStep.ApplyPolicy)]
    [InlineData(true, false, 1, 5, ProviderFailureStep.Release)]
    [InlineData(true, false, 4, 5, ProviderFailureStep.Release)]
    [InlineData(true, false, 5, 5, ProviderFailureStep.ApplyPolicy)]
    [InlineData(true, false, 6, 5, ProviderFailureStep.ApplyPolicy)]
    [InlineData(true, false, 1, 1, ProviderFailureStep.ApplyPolicy)]
    [InlineData(true, true, 1, 5, ProviderFailureStep.ApplyPolicy)]
    public void OnProviderFailure_NonRetryableKillSwitchOrCapAppliesPolicy(
        bool retryable, bool killSwitch, int failures, int maxAttempts, ProviderFailureStep expected)
    {
        Assert.Equal(expected, ProcessorBranch.OnProviderFailure(retryable, killSwitch, failures, maxAttempts));
    }

    [Theory]
    [InlineData(FormState.Approved, DecisionStatus.Approved)]
    [InlineData(FormState.Rejected, DecisionStatus.Quarantined)]
    [InlineData(FormState.Submitted, null)]
    [InlineData(FormState.Opened, null)]
    [InlineData(FormState.Deleted, null)]
    public void FromRecordState_MapsApprovedAndRejectedOnly(FormState state, DecisionStatus? expected)
    {
        Assert.Equal(expected, ProcessorBranch.FromRecordState(state));
    }

    [Fact]
    public void RemovalAudit_Missing_IsRecordDeleted()
    {
        Assert.Equal(
            (DecisionRepository.RecordDeletedAction, "Forms record missing"),
            ProcessorBranch.RemovalAudit(ProcessorStep.DeleteMissing, null));
    }

    [Fact]
    public void RemovalAudit_OtherState_IsDecisionRemovedWithState()
    {
        Assert.Equal(
            (DecisionRepository.DecisionRemovedAction, "record already Opened"),
            ProcessorBranch.RemovalAudit(ProcessorStep.DeleteOtherState, FormState.Opened));
    }

    [Fact]
    public void RemovalAudit_Unguarded_IsDecisionRemoved()
    {
        Assert.Equal(
            (DecisionRepository.DecisionRemovedAction, "form not guarded"),
            ProcessorBranch.RemovalAudit(ProcessorStep.SkipUnguarded, FormState.Submitted));
    }

    [Fact]
    public void RemovalAudit_NonDeletingStep_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProcessorBranch.RemovalAudit(ProcessorStep.Rule, FormState.Submitted));
    }
}

using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;

namespace Cogworks.Umbraco.FormsGuard.Tests.Persistence;

public class DecisionStampTests
{
    [Fact]
    public void NoQuestions_IsSettingsWithNoProviderAndNoAttempt()
    {
        var row = Row(attempts: 0, provider: "jev");

        var audit = DecisionRepository.StampWithoutCall(row, DecisionStatus.Review, "settings", "no questions to ask");

        Assert.Equal("Review", row.Status);
        Assert.Equal("settings", row.Source);
        Assert.Null(row.Provider);
        Assert.Equal(0, row.Attempts);
        Assert.Equal("Review by settings: no questions to ask", audit.Detail);
        Assert.Equal(DecisionRepository.SystemActor, audit.Actor);
        Assert.Equal("decision", audit.Action);
    }

    [Fact]
    public void KillSwitchBeforeCall_IsPolicyWithNoAttempt()
    {
        var row = Row(attempts: 0);

        var audit = DecisionRepository.StampWithoutCall(
            row, DecisionStatus.ApprovedNotChecked, "policy", "kill switch, no provider call");

        Assert.Equal("policy", row.Source);
        Assert.Equal(0, row.Attempts);
        Assert.Equal("ApprovedNotChecked by policy: kill switch, no provider call", audit.Detail);
    }

    [Theory]
    [InlineData("attempt cap")]
    [InlineData("non-retryable provider failure")]
    [InlineData("kill switch")]
    public void PolicyAfterCall_CountsAttemptAndNamesReason(string reason)
    {
        var row = Row(attempts: 2);

        var audit = DecisionRepository.StampByPolicy(row, DecisionStatus.Review, reason);

        Assert.Equal("policy", row.Source);
        Assert.Equal(3, row.Attempts);
        Assert.Equal($"Review by policy: {reason}", audit.Detail);
    }

    [Fact]
    public void Stamp_ClearsClaim()
    {
        var row = Row(attempts: 0);
        row.ClaimedBy = "node";
        row.ClaimedUtc = DateTime.UtcNow;
        row.NextAttemptUtc = DateTime.UtcNow;

        DecisionRepository.StampWithoutCall(row, DecisionStatus.Review, "settings", "no questions to ask");

        Assert.Null(row.ClaimedBy);
        Assert.Null(row.ClaimedUtc);
        Assert.Null(row.NextAttemptUtc);
    }

    private static DecisionDto Row(int attempts, string? provider = null) => new()
    {
        Id = 1,
        RecordId = Guid.NewGuid(),
        FormId = Guid.NewGuid(),
        Status = "Pending",
        Attempts = attempts,
        Provider = provider,
    };
}

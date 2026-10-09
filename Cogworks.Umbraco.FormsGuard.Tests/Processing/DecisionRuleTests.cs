using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Processing;
using Cogworks.Umbraco.FormsGuard.Settings;

namespace Cogworks.Umbraco.FormsGuard.Tests.Processing;

public class DecisionRuleTests
{
    private static readonly IReadOnlyList<DecisionQuestion> Core = new[]
    {
        new DecisionQuestion("guard.sales_pitch", "t", QuestionRole.SpamSignal),
        new DecisionQuestion("guard.automated", "t", QuestionRole.SpamSignal),
        new DecisionQuestion("guard.generic", "t", QuestionRole.Informational),
        new DecisionQuestion("guard.genuine", "t", QuestionRole.GenuineSignal),
    };

    private static readonly DecisionThresholds Defaults = DefaultFormSettings.Thresholds;

    private static DecisionAnswer[] Answers(double spam, double genuine, double other = 0.0) => new[]
    {
        new DecisionAnswer("guard.sales_pitch", spam),
        new DecisionAnswer("guard.automated", other),
        new DecisionAnswer("guard.generic", 1.0),
        new DecisionAnswer("guard.genuine", genuine),
    };

    private static DecisionStatus Eval(DecisionAnswer[] answers, DecisionThresholds? t = null) =>
        DecisionRule.Evaluate(Core, answers, t ?? Defaults);

    [Fact]
    public void HighSpam_Quarantined_EvenWithHighGenuine() =>
        Assert.Equal(DecisionStatus.Quarantined, Eval(Answers(0.90, 0.95)));

    [Fact]
    public void LowSpamHighGenuine_Approved() =>
        Assert.Equal(DecisionStatus.Approved, Eval(Answers(0.10, 0.80)));

    [Fact]
    public void MiddleSpam_Review() =>
        Assert.Equal(DecisionStatus.Review, Eval(Answers(0.50, 0.95)));

    [Fact]
    public void LowSpamLowGenuine_Review() =>
        Assert.Equal(DecisionStatus.Review, Eval(Answers(0.05, 0.40)));

    [Fact]
    public void SpamAtQuarantineMin_Quarantined() =>
        Assert.Equal(DecisionStatus.Quarantined, Eval(Answers(0.85, 0.95)));

    [Fact]
    public void SpamAtApproveMax_GenuineAtMin_Approved() =>
        Assert.Equal(DecisionStatus.Approved, Eval(Answers(0.15, 0.70)));

    [Fact]
    public void SpamScore_IsHighestSpamSignal() =>
        Assert.Equal(DecisionStatus.Quarantined, Eval(Answers(0.0, 0.95, other: 0.90)));

    [Fact]
    public void ContributedRoleIgnored_DecidedByCoreOnly()
    {
        var questions = Core.Append(new DecisionQuestion("test.x", "t", QuestionRole.SpamSignal)).ToList();
        var answers = Answers(0.05, 0.90).Append(new DecisionAnswer("test.x", 0.99)).ToArray();

        Assert.Equal(DecisionStatus.Approved, DecisionRule.Evaluate(questions, answers, Defaults));
    }

    [Fact]
    public void NoSpamQuestions_SpamIsZero()
    {
        var core = new[] { new DecisionQuestion("guard.genuine", "t", QuestionRole.GenuineSignal) };
        var answers = new[] { new DecisionAnswer("guard.genuine", 0.80) };

        Assert.Equal(DecisionStatus.Approved, DecisionRule.Evaluate(core, answers, Defaults));
    }

    [Fact]
    public void NoGenuineQuestions_GenuineIsZero_Review()
    {
        var core = new[] { new DecisionQuestion("guard.sales_pitch", "t", QuestionRole.SpamSignal) };
        var answers = new[] { new DecisionAnswer("guard.sales_pitch", 0.0) };

        Assert.Equal(DecisionStatus.Review, DecisionRule.Evaluate(core, answers, Defaults));
    }

    [Fact]
    public void NoQuestions_Review() =>
        Assert.Equal(DecisionStatus.Review, DecisionRule.Evaluate(Array.Empty<DecisionQuestion>(), Array.Empty<DecisionAnswer>(), Defaults));

    [Theory]
    [InlineData(0.60, 0.95, DecisionStatus.Quarantined)]
    [InlineData(0.30, 0.50, DecisionStatus.Approved)]
    [InlineData(0.45, 0.95, DecisionStatus.Review)]
    [InlineData(0.30, 0.49, DecisionStatus.Review)]
    public void CustomThresholds_Applied(double spam, double genuine, DecisionStatus expected) =>
        Assert.Equal(expected, Eval(Answers(spam, genuine), new DecisionThresholds(0.60, 0.30, 0.50)));
}

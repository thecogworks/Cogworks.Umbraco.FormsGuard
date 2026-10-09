using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Settings;

namespace Cogworks.Umbraco.FormsGuard.Processing;

/// <summary>Maps provider answers and a form's thresholds to Approved, Quarantined or Review.</summary>
public static class DecisionRule
{
    /// <summary>
    /// Evaluates the rule. Only core questions (keys starting <c>guard.</c>) are considered, so
    /// contributed questions never affect the verdict, whatever role they declare.
    /// </summary>
    public static DecisionStatus Evaluate(
        IReadOnlyList<DecisionQuestion> questions,
        IReadOnlyList<DecisionAnswer> answers,
        DecisionThresholds thresholds)
    {
        var spam = MaxFor(QuestionRole.SpamSignal, questions, answers);
        var genuine = MaxFor(QuestionRole.GenuineSignal, questions, answers);

        if (spam >= thresholds.QuarantineSpamMin)
        {
            return DecisionStatus.Quarantined;
        }

        if (spam <= thresholds.ApproveSpamMax && genuine >= thresholds.ApproveGenuineMin)
        {
            return DecisionStatus.Approved;
        }

        return DecisionStatus.Review;
    }

    private static double MaxFor(
        QuestionRole role,
        IReadOnlyList<DecisionQuestion> questions,
        IReadOnlyList<DecisionAnswer> answers)
    {
        var keys = questions
            .Where(q => q.Role == role && q.Key.StartsWith(QuestionMerger.ReservedPrefix, StringComparison.Ordinal)).Select(q => q.Key).ToHashSet(StringComparer.Ordinal);
        return answers
            .Where(a => keys.Contains(a.Key))
            .Select(a => a.Probability)
            .DefaultIfEmpty(0.0)
            .Max();
    }
}

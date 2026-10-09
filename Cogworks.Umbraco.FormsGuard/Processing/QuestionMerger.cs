using Cogworks.Umbraco.FormsGuard.Decisions;

namespace Cogworks.Umbraco.FormsGuard.Processing;

/// <summary>A contributed question that was dropped, or a contributor that threw (no key).</summary>
/// <param name="Source">The contributor's type name.</param>
/// <param name="Key">The rejected key; <c>null</c> when the contributor threw.</param>
/// <param name="Reason">Why it was rejected; for a throw, the exception type name.</param>
public sealed record QuestionRejection(string Source, string? Key, string Reason);

/// <summary>The merged question list for one entry plus everything that was dropped.</summary>
public sealed record MergedQuestions(
    IReadOnlyList<DecisionQuestion> Questions,
    IReadOnlyList<QuestionRejection> Rejections);

/// <summary>Builds one question list per entry: core questions first, then contributed ones in order.</summary>
public static class QuestionMerger
{
    public const string ReservedPrefix = QuestionKeys.GuardPrefix;

    public static MergedQuestions Merge(
        IReadOnlyList<DecisionQuestion> core,
        IEnumerable<(string Source, Func<IEnumerable<DecisionQuestion>> Get)> contributions)
    {
        var merged = new List<DecisionQuestion>(core);
        var keys = new HashSet<string>(core.Select(q => q.Key), StringComparer.Ordinal);
        var rejections = new List<QuestionRejection>();

        foreach (var (source, get) in contributions)
        {
            List<DecisionQuestion> contributed;
            try
            {
                contributed = (get() ?? Enumerable.Empty<DecisionQuestion>()).ToList();
            }
            catch (Exception ex)
            {
                rejections.Add(new QuestionRejection(source, null, ex.GetType().Name));
                continue;
            }

            foreach (var question in contributed)
            {
                if (question is null)
                {
                    continue;
                }

                var key = question.Key ?? string.Empty;
                string? reason =
                    !QuestionKeys.IsValid(key) ? "invalid key" :
                    key.StartsWith(ReservedPrefix, StringComparison.Ordinal) ? "reserved namespace" :
                    !keys.Add(key) ? "duplicate key" :
                    null;

                if (reason is null)
                {
                    merged.Add(question);
                }
                else
                {
                    rejections.Add(new QuestionRejection(source, key, reason));
                }
            }
        }

        return new MergedQuestions(merged, rejections);
    }
}

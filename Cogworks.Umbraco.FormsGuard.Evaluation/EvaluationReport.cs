using System.Globalization;
using System.Text;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Processing;
using Cogworks.Umbraco.FormsGuard.Settings;

namespace Cogworks.Umbraco.FormsGuard.Evaluation;

/// <summary>
/// The endpoint's answer for one corpus row. A failed row has <see cref="Success"/> false and an error.
/// Token counts are null when the provider did not report them.
/// </summary>
public sealed record RowResult(
    CorpusRow Row,
    bool Success,
    string? Error,
    double? SpamProbability,
    double? GenuineProbability,
    long ElapsedMs = 0,
    long? InputTokens = null,
    long? OutputTokens = null);

/// <summary>Error rates over the successful rows.</summary>
public sealed record EvaluationMetrics(
    int GenuineTotal,
    int GenuineQuarantined,
    int SpamTotal,
    int SpamApproved,
    int SpamQuarantined,
    int Scored,
    int Review,
    IReadOnlyList<RowResult> Errors);

/// <summary>Suggested thresholds; <see cref="Clamped"/> is true when ApproveSpamMax was lowered to stay below QuarantineSpamMin.</summary>
public sealed record ThresholdSuggestion(DecisionThresholds Thresholds, bool Clamped);

/// <summary>Pure metrics, threshold suggestion and report text for one provider run.</summary>
public static class EvaluationReport
{
    private const string Smoke =
        "Note: a 9-row corpus is a smoke test, not evidence for setting thresholds.";

    private static readonly IReadOnlyList<DecisionQuestion> BandQuestions = new[]
    {
        new DecisionQuestion("guard.spam", "spam", QuestionRole.SpamSignal),
        new DecisionQuestion("guard.genuine", "genuine", QuestionRole.GenuineSignal),
    };

    /// <summary>Bands a scored row through the real <see cref="DecisionRule"/>.</summary>
    public static DecisionStatus BandFor(double spam, double genuine, DecisionThresholds thresholds) =>
        DecisionRule.Evaluate(
            BandQuestions,
            new[] { new DecisionAnswer("guard.spam", spam), new DecisionAnswer("guard.genuine", genuine) },
            thresholds);

    public static EvaluationMetrics Compute(IReadOnlyList<RowResult> results, DecisionThresholds thresholds)
    {
        var scored = Scored(results);
        var genuine = scored.Where(r => !r.Row.IsSpam).ToList();
        var spam = scored.Where(r => r.Row.IsSpam).ToList();

        DecisionStatus Band(RowResult r) => BandFor(r.SpamProbability!.Value, r.GenuineProbability!.Value, thresholds);

        return new EvaluationMetrics(
            GenuineTotal: genuine.Count,
            GenuineQuarantined: genuine.Count(r => Band(r) == DecisionStatus.Quarantined),
            SpamTotal: spam.Count,
            SpamApproved: spam.Count(r => Band(r) == DecisionStatus.Approved),
            SpamQuarantined: spam.Count(r => Band(r) == DecisionStatus.Quarantined),
            Scored: scored.Count,
            Review: scored.Count(r => Band(r) == DecisionStatus.Review),
            Errors: results.Where(r => !IsScored(r)).ToList());
    }

    /// <summary>
    /// Suggests thresholds from the successful rows; a value with no rows to base it on keeps <paramref name="current"/>.
    /// The result always satisfies ApproveSpamMax &lt; QuarantineSpamMin.
    /// </summary>
    public static ThresholdSuggestion Suggest(IReadOnlyList<RowResult> results, DecisionThresholds current)
    {
        var scored = Scored(results);
        var genuine = scored.Where(r => !r.Row.IsSpam).ToList();
        var spam = scored.Where(r => r.Row.IsSpam).ToList();

        var quarantine = genuine.Count == 0
            ? current.QuarantineSpamMin
            : Math.Min(1.0, Round(genuine.Max(r => r.SpamProbability!.Value) + 0.01));

        var approveSpam = spam.Count == 0
            ? current.ApproveSpamMax
            : Math.Max(0.0, Round(spam.Min(r => r.SpamProbability!.Value) - 0.01));

        var approveGenuine = genuine.Count == 0
            ? current.ApproveGenuineMin
            : Math.Floor(Math.Round(genuine.Min(r => r.GenuineProbability!.Value) * 100, 6)) / 100;

        var ceiling = Math.Max(0.0, Round(quarantine - 0.01));
        var clamped = approveSpam > ceiling;
        if (clamped)
        {
            approveSpam = ceiling;
        }

        return new ThresholdSuggestion(new DecisionThresholds(quarantine, approveSpam, approveGenuine), clamped);
    }

    public static string Render(string provider, IReadOnlyList<RowResult> results, DecisionThresholds current)
    {
        var metrics = Compute(results, current);
        var sb = new StringBuilder();

        sb.AppendLine($"Genuine wrongly quarantined: {metrics.GenuineQuarantined}/{metrics.GenuineTotal}   (provider '{provider}', default thresholds)");
        sb.AppendLine($"Spam wrongly approved:       {metrics.SpamApproved}/{metrics.SpamTotal}");
        sb.AppendLine($"Sent to review:              {metrics.Review}/{metrics.Scored}");
        sb.AppendLine($"Errors:                      {metrics.Errors.Count}/{results.Count}");
        foreach (var error in metrics.Errors)
        {
            sb.AppendLine($"  line {error.Row.Line} ({error.Row.Label}, {error.Row.Name}): {error.Error ?? "unknown error"}");
        }

        sb.AppendLine();
        sb.AppendLine("Rows:");
        foreach (var r in results.Where(IsScored))
        {
            var band = BandFor(r.SpamProbability!.Value, r.GenuineProbability!.Value, current);
            sb.AppendLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {r.Row.Label,-7} {r.Row.Category,-12} spam {r.SpamProbability:0.00} genuine {r.GenuineProbability:0.00} -> {band,-11} {r.ElapsedMs} ms  {r.Row.Name}"));
        }

        sb.AppendLine();
        if (metrics.Scored == 0)
        {
            sb.AppendLine("No successful rows; no thresholds suggested.");
        }
        else
        {
            var suggestion = Suggest(results, current);
            var s = suggestion.Thresholds;
            sb.AppendLine("Suggested thresholds (current default in brackets):");
            sb.AppendLine(F($"  QuarantineSpamMin  {s.QuarantineSpamMin:0.00}  ({current.QuarantineSpamMin:0.00})"));
            sb.AppendLine(F($"  ApproveSpamMax     {s.ApproveSpamMax:0.00}  ({current.ApproveSpamMax:0.00})"));
            sb.AppendLine(F($"  ApproveGenuineMin  {s.ApproveGenuineMin:0.00}  ({current.ApproveGenuineMin:0.00})"));
            if (suggestion.Clamped)
            {
                sb.AppendLine(F($"  ApproveSpamMax was lowered to {s.ApproveSpamMax:0.00} to stay below QuarantineSpamMin."));
            }

            var suggested = Compute(results, s);
            sb.AppendLine(
                $"Spam quarantined: current {metrics.SpamQuarantined}/{metrics.SpamTotal}, suggested {suggested.SpamQuarantined}/{suggested.SpamTotal}; " +
                $"the suggested set misses {suggested.SpamTotal - suggested.SpamQuarantined} spam row(s).");
        }

        sb.AppendLine();
        sb.AppendLine(Smoke);
        return sb.ToString();
    }

    internal static bool IsScored(RowResult r) => r.Success && r.SpamProbability is not null && r.GenuineProbability is not null;

    private static List<RowResult> Scored(IReadOnlyList<RowResult> results) => results.Where(IsScored).ToList();

    private static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    internal static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}

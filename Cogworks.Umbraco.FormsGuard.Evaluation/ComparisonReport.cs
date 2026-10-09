using System.Globalization;
using System.Text;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Settings;

namespace Cogworks.Umbraco.FormsGuard.Evaluation;

/// <summary>USD per million tokens. A missing output price is 0.</summary>
public sealed record TokenPrice(double InputPerMillion, double OutputPerMillion = 0)
{
    /// <summary>Parses <c>alias=input[/output]</c>; throws <see cref="ArgumentException"/> on a bad or negative value.</summary>
    public static (string Alias, TokenPrice Price) Parse(string value)
    {
        var eq = value.IndexOf('=');
        var alias = eq > 0 ? value[..eq].Trim() : string.Empty;
        if (alias.Length == 0)
        {
            throw new ArgumentException($"--price '{value}' must be <alias>=<input usd per million>[/<output usd per million>].");
        }

        var parts = value[(eq + 1)..].Split('/');
        double output = 0;
        if (parts.Length > 2 || !TryUsd(parts[0], out var input) || (parts.Length == 2 && !TryUsd(parts[1], out output)))
        {
            throw new ArgumentException($"--price '{value}' must be <alias>=<input usd per million>[/<output usd per million>] with non-negative numbers.");
        }

        return (alias, new TokenPrice(input, output));

        static bool TryUsd(string text, out double usd) =>
            double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out usd) && double.IsFinite(usd) && usd >= 0;
    }
}

/// <summary>One provider's results in a comparison; <see cref="Price"/> is null when no price is known.</summary>
public sealed record ProviderRun(string Alias, IReadOnlyList<RowResult> Results, TokenPrice? Price);

/// <summary>Correct rows out of scored rows: genuine Approved plus spam Quarantined. Review is not correct.</summary>
public sealed record AccuracyResult(int Correct, int Scored);

/// <summary>Pure side-by-side metrics and table for several provider runs over the same corpus.</summary>
public static class ComparisonReport
{
    public static AccuracyResult Accuracy(IReadOnlyList<RowResult> results, DecisionThresholds thresholds)
    {
        var scored = results.Where(EvaluationReport.IsScored).ToList();
        var correct = scored.Count(r =>
            EvaluationReport.BandFor(r.SpamProbability!.Value, r.GenuineProbability!.Value, thresholds)
                == (r.Row.IsSpam ? DecisionStatus.Quarantined : DecisionStatus.Approved));
        return new AccuracyResult(correct, scored.Count);
    }

    /// <summary>Median <see cref="RowResult.ElapsedMs"/> over successful rows; null when there are none.</summary>
    public static double? MedianLatency(IReadOnlyList<RowResult> results)
    {
        var times = results.Where(EvaluationReport.IsScored).Select(r => r.ElapsedMs).Order().ToList();
        if (times.Count == 0)
        {
            return null;
        }

        var mid = times.Count / 2;
        return times.Count % 2 == 1 ? times[mid] : (times[mid - 1] + times[mid]) / 2.0;
    }

    /// <summary>
    /// Mean USD cost over successful rows that reported an input token count. Null when there is no price
    /// or no such row. A missing output count counts as 0.
    /// </summary>
    public static double? CostPerEntry(IReadOnlyList<RowResult> results, TokenPrice? price)
    {
        if (price is null)
        {
            return null;
        }

        var costs = results
            .Where(r => EvaluationReport.IsScored(r) && r.InputTokens is not null)
            .Select(r => (r.InputTokens!.Value * price.InputPerMillion + (r.OutputTokens ?? 0) * price.OutputPerMillion) / 1_000_000)
            .ToList();
        return costs.Count == 0 ? null : costs.Average();
    }

    public static string Render(IReadOnlyList<ProviderRun> runs, DecisionThresholds thresholds)
    {
        var header = new List<string> { "Metric" };
        var rows = new List<List<string>>
        {
            new() { "Accuracy" },
            new() { "Scored" },
            new() { "Genuine wrongly quarantined" },
            new() { "Spam wrongly approved" },
            new() { "Sent to review" },
            new() { "Errors" },
            new() { "Cost per entry (USD)" },
            new() { "Median latency" },
        };

        foreach (var run in runs)
        {
            header.Add(run.Alias);
            var m = EvaluationReport.Compute(run.Results, thresholds);
            var none = m.Scored == 0;
            var accuracy = Accuracy(run.Results, thresholds);
            var cost = CostPerEntry(run.Results, run.Price);
            var latency = MedianLatency(run.Results);

            rows[0].Add(none ? "n/a" : EvaluationReport.F($"{accuracy.Correct}/{accuracy.Scored} ({(double)accuracy.Correct / accuracy.Scored:0%})"));
            rows[1].Add(EvaluationReport.F($"{m.Scored}/{run.Results.Count}"));
            rows[2].Add(none ? "n/a" : EvaluationReport.F($"{m.GenuineQuarantined}/{m.GenuineTotal}"));
            rows[3].Add(none ? "n/a" : EvaluationReport.F($"{m.SpamApproved}/{m.SpamTotal}"));
            rows[4].Add(none ? "n/a" : EvaluationReport.F($"{m.Review}/{m.Scored}"));
            rows[5].Add(EvaluationReport.F($"{m.Errors.Count}/{run.Results.Count}"));
            rows[6].Add(cost is { } c ? FormatCost(c) : run.Price is null ? "n/a (no price)" : "n/a (no usage)");
            rows[7].Add(latency is { } l ? EvaluationReport.F($"{l:0.#} ms") : "n/a");
        }

        var all = new List<List<string>> { header };
        all.AddRange(rows);
        var widths = Enumerable.Range(0, header.Count).Select(i => all.Max(r => r[i].Length)).ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("Side-by-side (default thresholds):");
        foreach (var row in all)
        {
            sb.AppendLine(string.Join("  ", row.Select((cell, i) => cell.PadRight(widths[i]))).TrimEnd());
        }

        return sb.ToString();
    }

    /// <summary>Formats a cost with at least six decimals and enough to show two significant digits.</summary>
    public static string FormatCost(double cost)
    {
        if (cost <= 0)
        {
            return "0";
        }

        var decimals = Math.Clamp(1 - (int)Math.Floor(Math.Log10(cost)), 6, 15);
        return cost.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}

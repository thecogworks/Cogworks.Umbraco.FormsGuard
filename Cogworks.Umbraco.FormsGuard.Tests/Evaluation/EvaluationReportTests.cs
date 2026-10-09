using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Evaluation;
using Cogworks.Umbraco.FormsGuard.Settings;

namespace Cogworks.Umbraco.FormsGuard.Tests.Evaluation;

public class EvaluationReportTests
{
    private static readonly DecisionThresholds Defaults = DefaultFormSettings.Thresholds;

    private static RowResult Ok(string label, double spam, double genuine, int line = 2) =>
        new(new CorpusRow(line, label, "c", "n", "e", "m"), true, null, spam, genuine);

    private static RowResult Fail(string label, string error = "boom") =>
        new(new CorpusRow(9, label, "c", "n", "e", "m"), false, error, null, null);

    // 3 genuine, 6 spam: well separated.
    private static List<RowResult> Separable() => new()
    {
        Ok("genuine", 0.05, 0.95), Ok("genuine", 0.10, 0.80), Ok("genuine", 0.30, 0.734),
        Ok("spam", 0.99, 0.01), Ok("spam", 0.95, 0.02), Ok("spam", 0.90, 0.05),
        Ok("spam", 0.70, 0.20), Ok("spam", 0.65, 0.30), Ok("spam", 0.60, 0.40),
    };

    [Fact]
    public void Compute_counts_errors_against_default_bands()
    {
        var results = new List<RowResult>
        {
            Ok("genuine", 0.90, 0.10), // quarantined
            Ok("genuine", 0.05, 0.90), // approved
            Ok("genuine", 0.50, 0.50), // review
            Ok("spam", 0.10, 0.80),    // approved
            Ok("spam", 0.95, 0.00),    // quarantined
            Fail("spam"),
        };

        var m = EvaluationReport.Compute(results, Defaults);

        Assert.Equal(1, m.GenuineQuarantined);
        Assert.Equal(3, m.GenuineTotal);
        Assert.Equal(1, m.SpamApproved);
        Assert.Equal(2, m.SpamTotal);
        Assert.Equal(1, m.SpamQuarantined);
        Assert.Equal(1, m.Review);
        Assert.Equal(5, m.Scored);
        Assert.Single(m.Errors);
    }

    [Fact]
    public void BandFor_uses_the_real_rule()
    {
        Assert.Equal(DecisionStatus.Quarantined, EvaluationReport.BandFor(0.85, 1.0, Defaults));
        Assert.Equal(DecisionStatus.Approved, EvaluationReport.BandFor(0.15, 0.70, Defaults));
        Assert.Equal(DecisionStatus.Review, EvaluationReport.BandFor(0.16, 0.90, Defaults));
    }

    [Fact]
    public void Suggest_on_separable_corpus_clamps_approve_spam_max()
    {
        var s = EvaluationReport.Suggest(Separable(), Defaults);

        // Unclamped this would be 0.31 / 0.59, which FormSettingsValidator rejects.
        Assert.Equal(0.31, s.Thresholds.QuarantineSpamMin, 10);
        Assert.Equal(0.30, s.Thresholds.ApproveSpamMax, 10);
        Assert.Equal(0.73, s.Thresholds.ApproveGenuineMin, 10);
        Assert.True(s.Clamped);
    }

    [Fact]
    public void Suggest_rounds_genuine_min_down()
    {
        var s = EvaluationReport.Suggest(new List<RowResult> { Ok("genuine", 0.0, 0.29), Ok("spam", 0.9, 0.0) }, Defaults);

        Assert.Equal(0.29, s.Thresholds.ApproveGenuineMin, 10);

        s = EvaluationReport.Suggest(new List<RowResult> { Ok("genuine", 0.0, 0.739), Ok("spam", 0.9, 0.0) }, Defaults);
        Assert.Equal(0.73, s.Thresholds.ApproveGenuineMin, 10);
    }

    [Fact]
    public void Suggest_without_separable_cutoff_still_returns_a_valid_set()
    {
        var results = new List<RowResult>
        {
            Ok("genuine", 0.80, 0.60), // genuine scored spammier than some spam
            Ok("spam", 0.40, 0.30),
            Ok("spam", 0.95, 0.00),
        };

        var s = EvaluationReport.Suggest(results, Defaults);

        Assert.Equal(0.81, s.Thresholds.QuarantineSpamMin, 10);
        Assert.Equal(0.39, s.Thresholds.ApproveSpamMax, 10);
        Assert.False(s.Clamped);

        var text = EvaluationReport.Render("jev", results, Defaults);
        Assert.Contains("misses 1 spam row(s)", text);
    }

    [Fact]
    public void Suggest_clamps_when_spam_floor_exceeds_quarantine_cutoff()
    {
        var results = new List<RowResult> { Ok("genuine", 0.10, 0.90), Ok("spam", 0.80, 0.0) };

        var s = EvaluationReport.Suggest(results, Defaults);

        Assert.Equal(0.11, s.Thresholds.QuarantineSpamMin, 10);
        Assert.Equal(0.10, s.Thresholds.ApproveSpamMax, 10);
        Assert.True(s.Clamped);
        Assert.Contains("lowered to 0.10", EvaluationReport.Render("jev", results, Defaults));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(0.5, 0.505)]
    [InlineData(0.994, 0.006)]
    public void Suggested_set_always_has_approve_spam_max_below_quarantine_min(double genuineSpam, double spamSpam)
    {
        var s = EvaluationReport.Suggest(
            new List<RowResult> { Ok("genuine", genuineSpam, 0.5), Ok("spam", spamSpam, 0.5) }, Defaults);

        Assert.True(s.Thresholds.ApproveSpamMax < s.Thresholds.QuarantineSpamMin);
        Assert.InRange(s.Thresholds.QuarantineSpamMin, 0.0, 1.0);
        Assert.InRange(s.Thresholds.ApproveSpamMax, 0.0, 1.0);
    }

    [Fact]
    public void Failed_rows_are_excluded_from_suggestion()
    {
        var withFailure = Separable();
        withFailure.Add(Fail("genuine"));

        Assert.Equal(EvaluationReport.Suggest(Separable(), Defaults), EvaluationReport.Suggest(withFailure, Defaults));
    }

    [Fact]
    public void Render_puts_genuine_quarantined_first_lists_errors_and_notes_smoke_test()
    {
        var results = Separable();
        results.Add(Fail("spam", "No decision provider is registered with alias 'nope'"));

        var text = EvaluationReport.Render("jev", results, Defaults);
        var lines = text.Split('\n');

        Assert.StartsWith("Genuine wrongly quarantined: 0/3", lines[0]);
        Assert.Contains("Errors:                      1/10", text);
        Assert.Contains("alias 'nope'", text);
        Assert.Contains("ApproveSpamMax", text);
        Assert.Contains("(0.85)", text);
        Assert.Contains("smoke test", text);
    }

    [Fact]
    public void Render_with_only_errors_does_not_suggest()
    {
        var text = EvaluationReport.Render("nope", new List<RowResult> { Fail("spam"), Fail("genuine") }, Defaults);

        Assert.StartsWith("Genuine wrongly quarantined: 0/0", text);
        Assert.Contains("No successful rows", text);
    }
}

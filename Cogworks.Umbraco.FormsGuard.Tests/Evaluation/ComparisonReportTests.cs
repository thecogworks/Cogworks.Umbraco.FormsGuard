using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Evaluation;
using Cogworks.Umbraco.FormsGuard.Settings;

namespace Cogworks.Umbraco.FormsGuard.Tests.Evaluation;

public class ComparisonReportTests
{
    private static readonly DecisionThresholds Defaults = DefaultFormSettings.Thresholds;

    private static RowResult Ok(string label, double spam, double genuine, long ms = 100, long? input = null, long? output = null) =>
        new(new CorpusRow(2, label, "c", "n", "e", "m"), true, null, spam, genuine, ms, input, output);

    private static RowResult Fail(string label) =>
        new(new CorpusRow(9, label, "c", "n", "e", "m"), false, "boom", null, null, 5000, 1000, 1000);

    [Fact]
    public void Accuracy_counts_genuine_approved_and_spam_quarantined_only()
    {
        var results = new List<RowResult>
        {
            Ok("genuine", 0.05, 0.90), // approved: correct
            Ok("genuine", 0.50, 0.50), // review: not correct
            Ok("genuine", 0.90, 0.10), // quarantined: wrong
            Ok("spam", 0.95, 0.00),    // quarantined: correct
            Ok("spam", 0.10, 0.80),    // approved: wrong
            Fail("spam"),              // excluded
        };

        Assert.Equal(new AccuracyResult(2, 5), ComparisonReport.Accuracy(results, Defaults));
    }

    [Fact]
    public void MedianLatency_odd_takes_middle_and_ignores_failures()
    {
        var results = new List<RowResult> { Ok("spam", 0.9, 0, 300), Ok("spam", 0.9, 0, 100), Ok("spam", 0.9, 0, 200), Fail("spam") };

        Assert.Equal(200, ComparisonReport.MedianLatency(results));
    }

    [Fact]
    public void MedianLatency_even_takes_mean_of_middle_two()
    {
        var results = new List<RowResult> { Ok("spam", 0.9, 0, 400), Ok("spam", 0.9, 0, 100), Ok("spam", 0.9, 0, 200), Ok("spam", 0.9, 0, 205) };

        Assert.Equal(202.5, ComparisonReport.MedianLatency(results));
    }

    [Fact]
    public void MedianLatency_without_successful_rows_is_null()
    {
        Assert.Null(ComparisonReport.MedianLatency(new List<RowResult> { Fail("spam") }));
    }

    [Fact]
    public void CostPerEntry_prices_input_and_output_and_skips_rows_without_usage()
    {
        var results = new List<RowResult>
        {
            Ok("spam", 0.9, 0, input: 1000, output: 200), // 1000*1 + 200*5 = 2000 / 1e6
            Ok("spam", 0.9, 0, input: 3000),              // output missing counts as 0: 3000 / 1e6
            Ok("spam", 0.9, 0),                           // no usage: excluded
            Fail("spam"),                                 // failed: excluded
        };

        var cost = ComparisonReport.CostPerEntry(results, new TokenPrice(1.00, 5.00));

        Assert.NotNull(cost);
        Assert.Equal(0.0025, cost!.Value, 12);
    }

    [Fact]
    public void CostPerEntry_is_null_without_price_or_usage()
    {
        Assert.Null(ComparisonReport.CostPerEntry(new List<RowResult> { Ok("spam", 0.9, 0, input: 1000) }, null));
        Assert.Null(ComparisonReport.CostPerEntry(new List<RowResult> { Ok("spam", 0.9, 0) }, new TokenPrice(1, 1)));
    }

    [Fact]
    public void FormatCost_keeps_small_values_non_zero()
    {
        Assert.Equal("0.0000080", ComparisonReport.FormatCost(0.000008));
        Assert.Equal("0.000600", ComparisonReport.FormatCost(0.0006));
        Assert.Equal("0.000000042", ComparisonReport.FormatCost(0.000000042));
    }

    [Fact]
    public void Render_shows_one_column_per_provider_and_na_for_all_failed_column()
    {
        var jev = new List<RowResult>
        {
            Ok("genuine", 0.05, 0.90, 200, 100), Ok("spam", 0.95, 0.00, 220, 100), Ok("spam", 0.50, 0.50, 210, 100),
        };
        var umbracoai = new List<RowResult> { Fail("genuine"), Fail("spam"), Fail("spam") };

        var text = ComparisonReport.Render(
            new[]
            {
                new ProviderRun("jev", jev, new TokenPrice(0.042)),
                new ProviderRun("umbracoai", umbracoai, new TokenPrice(1.00, 5.00)),
            },
            Defaults);
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        Assert.Matches(@"^Metric\s+jev\s+umbracoai$", lines[1]);
        Assert.Matches(@"^Accuracy\s+2/3 \(67%\)\s+n/a$", Line("Accuracy"));
        Assert.Matches(@"^Scored\s+3/3\s+0/3$", Line("Scored"));
        Assert.Matches(@"^Genuine wrongly quarantined\s+0/1\s+n/a$", Line("Genuine"));
        Assert.Matches(@"^Spam wrongly approved\s+0/2\s+n/a$", Line("Spam"));
        Assert.Matches(@"^Sent to review\s+1/3\s+n/a$", Line("Sent"));
        Assert.Matches(@"^Errors\s+0/3\s+3/3$", Line("Errors"));
        Assert.Matches(@"^Cost per entry \(USD\)\s+0\.0000042\s+n/a \(no usage\)$", Line("Cost"));
        Assert.Matches(@"^Median latency\s+210 ms\s+n/a$", Line("Median"));

        string Line(string start) => lines.Single(l => l.StartsWith(start, StringComparison.Ordinal));
    }

    [Fact]
    public void Render_without_price_shows_cost_na()
    {
        var text = ComparisonReport.Render(
            new[]
            {
                new ProviderRun("jev", new List<RowResult> { Ok("spam", 0.9, 0, input: 100) }, new TokenPrice(0.042)),
                new ProviderRun("umbracoai", new List<RowResult> { Ok("spam", 0.9, 0, input: 100) }, null),
            },
            Defaults);

        Assert.Matches(@"Cost per entry \(USD\)\s+\S+\s+n/a \(no price\)", text);
    }

    [Theory]
    [InlineData("umbracoai=1.00/5.00", "umbracoai", 1.00, 5.00)]
    [InlineData("jev=0.042", "jev", 0.042, 0)]
    public void TokenPrice_Parse_reads_input_and_optional_output(string value, string alias, double input, double output)
    {
        Assert.Equal((alias, new TokenPrice(input, output)), TokenPrice.Parse(value));
    }

    [Theory]
    [InlineData("umbracoai=abc")]
    [InlineData("umbracoai")]
    [InlineData("=1")]
    [InlineData("umbracoai=-1")]
    [InlineData("umbracoai=1/2/3")]
    public void TokenPrice_Parse_rejects_bad_values(string value)
    {
        Assert.Throws<ArgumentException>(() => TokenPrice.Parse(value));
    }
}

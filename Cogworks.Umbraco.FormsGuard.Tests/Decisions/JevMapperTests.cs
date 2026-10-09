using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Decisions.Jev;

namespace Cogworks.Umbraco.FormsGuard.Tests.Decisions;

public class JevMapperTests
{
    private static readonly DecisionQuestion Genuine = new(
        "guard.genuine", "Is this a genuine enquiry?", QuestionRole.GenuineSignal,
        TrueCriteria: "It is genuine.", FalseCriteria: "It is not genuine.");

    private static readonly DecisionQuestion Spam = new("guard.sales_pitch", "Selling?", QuestionRole.SpamSignal);

    private static readonly DecisionQuestion Team = new(
        "triage.team", "Which team?", QuestionRole.Informational, QuestionType.Choice,
        new Dictionary<string, string> { ["a"] = "Sales", ["b"] = "Support" });

    [Fact]
    public void BuildBody_HasDocumentedShape()
    {
        var request = new DecisionRequest(
            Guid.NewGuid(),
            new DecisionState("Cogworks", "Contact Us", null, new Dictionary<string, string> { ["Message"] = "Hello" }),
            new[] { Genuine, Spam, Team });

        var body = JevMapper.BuildBody(request, "jev-1.13.0");

        Assert.Equal(new[] { "state", "model", "questions" }, body.Select(p => p.Key));
        Assert.Equal("jev-1.13.0", (string?)body["model"]);

        var state = body["state"]!.AsObject();
        Assert.Equal("Cogworks", (string?)state["organisation"]);
        Assert.Equal("Contact Us", (string?)state["form"]);
        Assert.True(state.ContainsKey("emailDomain"));
        Assert.Null(state["emailDomain"]);
        Assert.Equal("Hello", (string?)state["fields"]!["Message"]);

        var questions = body["questions"]!.AsObject();
        Assert.Equal(new[] { "guard.genuine", "guard.sales_pitch", "triage.team" }, questions.Select(p => p.Key));

        var genuine = questions["guard.genuine"]!;
        Assert.Equal("noul", (string?)genuine["type"]);
        Assert.Equal("Is this a genuine enquiry?", (string?)genuine["instructions"]);
        Assert.Equal("It is genuine.", (string?)genuine["criteria"]!["true"]);
        Assert.Equal("It is not genuine.", (string?)genuine["criteria"]!["false"]);

        var spam = questions["guard.sales_pitch"]!.AsObject();
        Assert.Equal("noul", (string?)spam["type"]);
        Assert.False(spam.ContainsKey("criteria"));

        var team = questions["triage.team"]!;
        Assert.Equal("choice", (string?)team["type"]);
        Assert.Equal("Sales", (string?)team["criteria"]!["a"]);
        Assert.Equal("Support", (string?)team["criteria"]!["b"]);
    }

    [Fact]
    public void Parse_Success_OneAnswerPerQuestion()
    {
        const string json = """
            {"model":"jev-1.13.0","answers":{
              "guard.genuine":{"type":"noul","noul":0.92},
              "guard.sales_pitch":{"type":"noul","noul":0.03}}}
            """;

        var result = JevMapper.Parse(json, new[] { Genuine, Spam });

        Assert.True(result.Success);
        Assert.Equal("jev-1.13.0", result.ModelVersion);
        Assert.Equal(
            new[] { new DecisionAnswer("guard.genuine", 0.92), new DecisionAnswer("guard.sales_pitch", 0.03) },
            result.Answers);
    }

    [Fact]
    public void Parse_Choice_MapsChosenProbability()
    {
        const string json = """
            {"model":"jev-1.13.0","answers":{"triage.team":{"type":"choice","choice":"a","probabilities":{"a":0.8,"b":0.2}}}}
            """;

        var result = JevMapper.Parse(json, new[] { Team });

        Assert.True(result.Success);
        var answer = Assert.Single(result.Answers);
        Assert.Equal(0.8, answer.Probability);
        Assert.Equal("a", answer.ChoiceKey);
        Assert.Equal(0.8, answer.OptionProbabilities!["a"]);
        Assert.Equal(0.2, answer.OptionProbabilities!["b"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData((HttpStatusCode)422)]
    public void Classify_AuthOrValidation_NotRetryable(HttpStatusCode status)
    {
        var result = JevMapper.Classify(status, null, "req-123");

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Contains(((int)status).ToString(), result.Error);
        Assert.Contains("req-123", result.Error);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(529)]
    public void Classify_Transient_Retryable(int status)
    {
        var result = JevMapper.Classify((HttpStatusCode)status, null, null);

        Assert.False(result.Success);
        Assert.True(result.Retryable);
        Assert.Null(result.RetryAfter);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(529)]
    public void Classify_RetryAfterSeconds_SetsRetryAfter(int status)
    {
        var result = JevMapper.Classify((HttpStatusCode)status, new RetryConditionHeaderValue(TimeSpan.FromSeconds(7)), null);

        Assert.True(result.Retryable);
        Assert.Equal(TimeSpan.FromSeconds(7), result.RetryAfter);
    }

    [Fact]
    public void Classify_RetryAfterDate_SetsRetryAfter()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        var result = JevMapper.Classify((HttpStatusCode)429, new RetryConditionHeaderValue(now.AddSeconds(30)), null, now);

        Assert.Equal(TimeSpan.FromSeconds(30), result.RetryAfter);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"model":"jev-1.13.0"}""")]
    [InlineData("""{"model":"jev-1.13.0","answers":{"guard.genuine":{"type":"noul","noul":0.9}}}""")]
    [InlineData("""{"model":"jev-1.13.0","answers":{"guard.genuine":{"type":"noul","noul":"high"},"guard.sales_pitch":{"type":"noul","noul":0.1}}}""")]
    [InlineData("""{"model":"jev-1.13.0","answers":{"guard.genuine":{"type":"noul","noul":1.5},"guard.sales_pitch":{"type":"noul","noul":0.1},"triage.team":{"type":"choice","choice":"a","probabilities":{"a":0.8,"b":0.2}}}}""")]
    [InlineData("""{"model":"jev-1.13.0","answers":{"guard.genuine":{"type":"noul","noul":0.9},"guard.sales_pitch":{"type":"noul","noul":0.1},"triage.team":{"type":"choice","probabilities":{"a":0.8,"b":0.2}}}}""")]
    [InlineData("""{"model":"jev-1.13.0","answers":{"guard.genuine":{"type":"noul","noul":0.9},"guard.sales_pitch":{"type":"noul","noul":0.1},"triage.team":{"type":"choice","choice":"c","probabilities":{"a":0.8,"b":0.2}}}}""")]
    public void Parse_BadJson_FailedRetryable(string json)
    {
        var result = JevMapper.Parse(json, new[] { Genuine, Spam, Team });

        Assert.False(result.Success);
        Assert.True(result.Retryable);
        Assert.DoesNotContain("high", result.Error);
    }

    [Fact]
    public void Parse_DuplicateAnswerKey_FailsRetryable()
    {
        var json = """{"answers":{"guard.sales_pitch":{"noul":0.1},"guard.sales_pitch":{"noul":0.2}}}""";

        var result = JevMapper.Parse(json, new[] { Spam });

        Assert.False(result.Success);
        Assert.True(result.Retryable);
        Assert.Equal("Jev returned an unusable response (invalid JSON)", result.Error);
    }

    [Fact]
    public void BuildBody_BlankCriteria_Omitted()
    {
        var request = new DecisionRequest(
            Guid.NewGuid(),
            new DecisionState("Cogworks", "Contact Us", null, new Dictionary<string, string>()),
            new[]
            {
                new DecisionQuestion("guard.a", "A?", QuestionRole.SpamSignal, TrueCriteria: "  ", FalseCriteria: ""),
                new DecisionQuestion("guard.b", "B?", QuestionRole.SpamSignal, TrueCriteria: "Yes.", FalseCriteria: " "),
            });

        var questions = JevMapper.BuildBody(request, "jev-1.13.0")["questions"]!.AsObject();

        Assert.False(questions["guard.a"]!.AsObject().ContainsKey("criteria"));
        var criteria = questions["guard.b"]!["criteria"]!.AsObject();
        Assert.Equal("Yes.", (string?)criteria["true"]);
        Assert.False(criteria.ContainsKey("false"));
    }

    [Fact]
    public void Parse_UnknownAnswer_Ignored()
    {
        const string json = """
            {"model":"jev-1.13.0","answers":{"guard.genuine":{"type":"noul","noul":0.5},"guard.extra":{"type":"noul","noul":0.1}}}
            """;

        var result = JevMapper.Parse(json, new[] { Genuine });

        Assert.True(result.Success);
        Assert.Equal("guard.genuine", Assert.Single(result.Answers).Key);
    }

    [Fact]
    public void Parse_Usage_CarriesTokenCounts()
    {
        const string json = """
            {"model":"jev-1.13.0","answers":{"guard.sales_pitch":{"type":"noul","noul":0.03}},
             "usage":{"input_tokens":812,"output_tokens":34}}
            """;

        var result = JevMapper.Parse(json, new[] { Spam });

        Assert.True(result.Success);
        Assert.Equal(812, result.InputTokens);
        Assert.Equal(34, result.OutputTokens);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"usage\":null")]
    [InlineData(",\"usage\":\"lots\"")]
    [InlineData(",\"usage\":{\"input_tokens\":\"812\",\"output_tokens\":1.5}")]
    [InlineData(",\"usage\":{\"input_tokens\":-1}")]
    public void Parse_MissingOrMalformedUsage_GivesNullCountsNotFailure(string usage)
    {
        var json = "{\"answers\":{\"guard.sales_pitch\":{\"noul\":0.03}}" + usage + "}";

        var result = JevMapper.Parse(json, new[] { Spam });

        Assert.True(result.Success);
        Assert.Null(result.InputTokens);
        Assert.Null(result.OutputTokens);
    }
}

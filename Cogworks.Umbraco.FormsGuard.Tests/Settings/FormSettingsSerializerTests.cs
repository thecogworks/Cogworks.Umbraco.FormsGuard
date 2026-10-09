using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Settings;

namespace Cogworks.Umbraco.FormsGuard.Tests.Settings;

public class FormSettingsSerializerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void EmptyJson_GivesAllDefaults(string? json)
    {
        var (settings, valid) = FormSettingsSerializer.Parse(json);

        Assert.True(valid);
        AssertIsDefault(settings);
    }

    [Fact]
    public void PartialJson_KeepsOrganisation_RestDefault()
    {
        var (settings, valid) = FormSettingsSerializer.Parse("""{ "organisation": "A UK law firm." }""");

        Assert.True(valid);
        Assert.Equal("A UK law firm.", settings.Organisation);
        Assert.Null(settings.AllowedFieldIds);
        Assert.False(settings.SendEmailDomain);
        Assert.Equal(DefaultFormSettings.Questions, settings.Questions);
        Assert.Equal(DefaultFormSettings.Thresholds, settings.Thresholds);
        Assert.Equal(FailurePolicy.ApproveNotChecked, settings.FailurePolicy);
    }

    [Theory]
    [InlineData("""{ "questions": [] }""")]
    [InlineData("""{ "organisation": "x" }""")]
    [InlineData("""{ "questions": null }""")]
    public void EmptyOrMissingQuestions_GiveFiveDefaults(string json)
    {
        var (settings, valid) = FormSettingsSerializer.Parse(json);

        Assert.True(valid);
        Assert.Equal(DefaultFormSettings.Questions, settings.Questions);
        Assert.Equal(5, settings.Questions.Count);
    }

    [Fact]
    public void CustomQuestions_UsedAsStored()
    {
        const string json = """
            {
              "questions": [
                { "key": "guard.sales_pitch", "text": "Selling?", "role": "SpamSignal", "enabled": false },
                { "key": "guard.genuine", "text": "Genuine?", "role": "GenuineSignal", "enabled": true }
              ]
            }
            """;

        var (settings, valid) = FormSettingsSerializer.Parse(json);

        Assert.True(valid);
        Assert.Equal(
            new[]
            {
                new QuestionSetting("guard.sales_pitch", "Selling?", QuestionRole.SpamSignal, false),
                new QuestionSetting("guard.genuine", "Genuine?", QuestionRole.GenuineSignal, true),
            },
            settings.Questions);
    }

    [Fact]
    public void StoredQuestionsWithoutCriteria_ParseWithNullCriteria()
    {
        const string json = """
            { "questions": [ { "key": "guard.genuine", "text": "Genuine?", "role": "GenuineSignal", "enabled": true } ] }
            """;

        var (settings, valid) = FormSettingsSerializer.Parse(json);

        Assert.True(valid);
        var question = Assert.Single(settings.Questions);
        Assert.Null(question.TrueCriteria);
        Assert.Null(question.FalseCriteria);
    }

    [Fact]
    public void Criteria_RoundTrip()
    {
        var original = DefaultFormSettings.Create() with
        {
            Questions = new[]
            {
                new QuestionSetting("guard.genuine", "Genuine?", QuestionRole.GenuineSignal, true, "It is.", "It is not."),
            },
        };

        var (parsed, valid) = FormSettingsSerializer.Parse(FormSettingsSerializer.Serialize(original));

        Assert.True(valid);
        Assert.Equal(original.Questions, parsed.Questions);
    }

    [Fact]
    public void Defaults_AllHaveCriteria()
    {
        Assert.All(DefaultFormSettings.Questions, q =>
        {
            Assert.False(string.IsNullOrWhiteSpace(q.TrueCriteria));
            Assert.False(string.IsNullOrWhiteSpace(q.FalseCriteria));
        });
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("""{ "failurePolicy": "Explode" }""")]
    [InlineData("""{ "questions": [ { "key": "guard.x", "text": "t" } ] }""")]
    [InlineData("""{ "questions": [ null ] }""")]
    public void MalformedJson_GivesDefaults_NotValid(string json)
    {
        var (settings, valid) = FormSettingsSerializer.Parse(json);

        Assert.False(valid);
        AssertIsDefault(settings);
    }

    [Theory]
    [InlineData("organisation")]
    [InlineData("key")]
    [InlineData("threshold")]
    public void InvalidStoredText_GivesDefaults_NotValid(string problem)
    {
        var json = problem switch
        {
            "organisation" => $$"""{ "organisation": "{{new string('a', 1001)}}" }""",
            "key" => """{ "questions": [ { "key": "spam", "text": "t", "role": "SpamSignal" } ] }""",
            _ => """{ "thresholds": { "quarantineSpamMin": 1.5 } }""",
        };

        var (settings, valid) = FormSettingsSerializer.Parse(json);

        Assert.False(valid);
        AssertIsDefault(settings);
    }

    [Fact]
    public void PartialThresholds_FillMissingFromDefaults()
    {
        var (settings, valid) = FormSettingsSerializer.Parse("""{ "thresholds": { "approveGenuineMin": 0.5 } }""");

        Assert.True(valid);
        Assert.Equal(new DecisionThresholds(0.85, 0.15, 0.5), settings.Thresholds);
    }

    [Fact]
    public void SerializeThenParse_RoundTrips()
    {
        var fieldId = Guid.NewGuid();
        var original = new FormGuardSettings(
            Organisation: "A charity.\nWe help people.",
            AllowedFieldIds: new[] { fieldId },
            SendEmailDomain: true,
            Questions: new[]
            {
                new QuestionSetting("guard.sales_pitch", "Selling?", QuestionRole.SpamSignal, false),
                new QuestionSetting("guard.note", "Note?", QuestionRole.Informational, true),
            },
            Thresholds: new DecisionThresholds(0.9, 0.1, 0.6),
            FailurePolicy: FailurePolicy.Review,
            EmailFieldId: fieldId);

        var json = FormSettingsSerializer.Serialize(original);
        var (parsed, valid) = FormSettingsSerializer.Parse(json);

        Assert.True(valid);
        Assert.Contains("\"failurePolicy\":\"Review\"", json);
        Assert.Contains("\"role\":\"SpamSignal\"", json);
        Assert.Equal(original.Organisation, parsed.Organisation);
        Assert.Equal(original.AllowedFieldIds, parsed.AllowedFieldIds);
        Assert.Equal(original.SendEmailDomain, parsed.SendEmailDomain);
        Assert.Equal(original.Questions, parsed.Questions);
        Assert.Equal(original.Thresholds, parsed.Thresholds);
        Assert.Equal(original.FailurePolicy, parsed.FailurePolicy);
        Assert.Contains($"\"emailFieldId\":\"{fieldId}\"", json);
        Assert.Equal(original.EmailFieldId, parsed.EmailFieldId);
    }

    private static void AssertIsDefault(FormGuardSettings settings)
    {
        var defaults = DefaultFormSettings.Create();
        Assert.Equal(defaults.Organisation, settings.Organisation);
        Assert.Null(settings.AllowedFieldIds);
        Assert.Equal(defaults.SendEmailDomain, settings.SendEmailDomain);
        Assert.Equal(defaults.Questions, settings.Questions);
        Assert.Equal(defaults.Thresholds, settings.Thresholds);
        Assert.Equal(defaults.FailurePolicy, settings.FailurePolicy);
        Assert.Null(settings.EmailFieldId);
    }
}

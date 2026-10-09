using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Settings;

namespace Cogworks.Umbraco.FormsGuard.Tests.Settings;

public class FormSettingsValidatorTests
{
    [Fact]
    public void ShippedDefaults_AreValid_AndMatchSpec()
    {
        var defaults = DefaultFormSettings.Create();

        Assert.Empty(FormSettingsValidator.Validate(defaults));
        Assert.Equal(string.Empty, defaults.Organisation);
        Assert.Null(defaults.AllowedFieldIds);
        Assert.False(defaults.SendEmailDomain);
        Assert.Equal(new DecisionThresholds(0.85, 0.15, 0.70), defaults.Thresholds);
        Assert.Equal(FailurePolicy.ApproveNotChecked, defaults.FailurePolicy);

        Assert.Collection(
            defaults.Questions,
            q => AssertQuestion(q, "guard.sales_pitch", QuestionRole.SpamSignal,
                "Is this an unsolicited offer to sell products or services to the organisation, such as SEO, web design, marketing, lead generation, staffing or software?"),
            q => AssertQuestion(q, "guard.automated", QuestionRole.SpamSignal,
                "Does this look machine-generated or templated, such as nonsense text, keyword stuffing or placeholder text?"),
            q => AssertQuestion(q, "guard.phishing", QuestionRole.SpamSignal,
                "Does this message try to get the reader to click a link, download a file, or share login, payment or personal details?"),
            q => AssertQuestion(q, "guard.generic", QuestionRole.Informational,
                "Could this message have been sent unchanged to almost any organisation?"),
            q => AssertQuestion(q, "guard.genuine", QuestionRole.GenuineSignal,
                "Is this a genuine enquiry or request from a person who wants something from the organisation described?"));
    }

    [Fact]
    public void OverLongOrganisation_IsOneError()
    {
        var settings = DefaultFormSettings.Create() with { Organisation = new string('a', 1001) };

        Assert.Single(FormSettingsValidator.Validate(settings));
    }

    [Fact]
    public void OrganisationAtLimit_WithNewlinesAndTabs_IsValid()
    {
        var settings = DefaultFormSettings.Create() with { Organisation = "a\r\nb\tc" + new string('a', 994) };

        Assert.Empty(FormSettingsValidator.Validate(settings));
    }

    [Fact]
    public void ControlCharacters_AreErrors()
    {
        var settings = DefaultFormSettings.Create() with
        {
            Organisation = "bad\u0007",
            Questions = new[] { new QuestionSetting("guard.x", "bad\u0000text", QuestionRole.SpamSignal, true) },
        };

        Assert.Equal(2, FormSettingsValidator.Validate(settings).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyQuestionText_IsError(string text)
    {
        var settings = WithQuestions(new QuestionSetting("guard.x", text, QuestionRole.SpamSignal, true));

        Assert.Single(FormSettingsValidator.Validate(settings));
    }

    [Fact]
    public void OverLongQuestionText_IsError()
    {
        var settings = WithQuestions(new QuestionSetting("guard.x", new string('q', 501), QuestionRole.SpamSignal, true));

        Assert.Single(FormSettingsValidator.Validate(settings));
    }

    [Theory]
    [InlineData("spam")]
    [InlineData("triage.team")]
    [InlineData("guard.")]
    [InlineData("guard.Upper")]
    [InlineData("guard.1st")]
    [InlineData("guard.has-dash")]
    [InlineData("guard.x\n")]
    public void BadKey_IsError(string key)
    {
        var settings = WithQuestions(new QuestionSetting(key, "Text?", QuestionRole.SpamSignal, true));

        Assert.Single(FormSettingsValidator.Validate(settings));
    }

    [Fact]
    public void KeyAtMaxLength_IsValid()
    {
        var settings = WithQuestions(new QuestionSetting("guard.a" + new string('b', 48), "Text?", QuestionRole.SpamSignal, true));

        Assert.Empty(FormSettingsValidator.Validate(settings));
    }

    [Fact]
    public void DuplicateKey_IsOneError()
    {
        var settings = WithQuestions(
            new QuestionSetting("guard.x", "One?", QuestionRole.SpamSignal, true),
            new QuestionSetting("guard.x", "Two?", QuestionRole.GenuineSignal, true));

        Assert.Single(FormSettingsValidator.Validate(settings));
    }

    [Theory]
    [InlineData(-0.1, 0.15, 0.7)]
    [InlineData(0.85, 1.1, 0.7)]
    [InlineData(0.85, 0.15, 2)]
    [InlineData(0.85, 0.15, double.NaN)]
    public void ThresholdOutsideUnitRange_IsError(double quarantine, double approveSpam, double approveGenuine)
    {
        var settings = DefaultFormSettings.Create() with
        {
            Thresholds = new DecisionThresholds(quarantine, approveSpam, approveGenuine),
        };

        Assert.NotEmpty(FormSettingsValidator.Validate(settings));
    }

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(0.5, 0.6)]
    public void ApproveSpamMaxNotBelowQuarantine_IsError(double quarantine, double approveSpam)
    {
        var settings = DefaultFormSettings.Create() with
        {
            Thresholds = new DecisionThresholds(quarantine, approveSpam, 0.7),
        };

        Assert.Single(FormSettingsValidator.Validate(settings));
    }

    [Fact]
    public void SeveralProblems_GiveOneErrorEach()
    {
        var settings = new FormGuardSettings(
            Organisation: new string('a', 1001),
            AllowedFieldIds: null,
            SendEmailDomain: false,
            Questions: new[]
            {
                new QuestionSetting("bad", "Text?", QuestionRole.SpamSignal, true),
                new QuestionSetting("guard.ok", new string('q', 501), QuestionRole.SpamSignal, true),
            },
            Thresholds: new DecisionThresholds(0.85, 0.15, 1.5),
            FailurePolicy: FailurePolicy.Review);

        Assert.Equal(4, FormSettingsValidator.Validate(settings).Count);
    }

    [Fact]
    public void TwentyQuestions_AreValid_TwentyOneAreAnError()
    {
        var twenty = Enumerable.Range(0, 20)
            .Select(i => new QuestionSetting($"guard.q{i}", "Text", QuestionRole.SpamSignal, true))
            .ToArray();
        Assert.Empty(FormSettingsValidator.Validate(WithQuestions(twenty)));

        var twentyOne = twenty.Append(new QuestionSetting("guard.q20", "Text", QuestionRole.SpamSignal, true)).ToArray();
        Assert.Single(FormSettingsValidator.Validate(WithQuestions(twentyOne)));
    }

    [Theory]
    [InlineData("guard.sales_pitch", true, true)]
    [InlineData("acme.extra", true, false)]
    [InlineData("guard.", false, false)]
    [InlineData("Guard.x", false, false)]
    [InlineData("noprefix", false, false)]
    public void QuestionKeys_SharedPattern(string key, bool valid, bool guardKey)
    {
        Assert.Equal(valid, QuestionKeys.IsValid(key));
        Assert.Equal(guardKey, QuestionKeys.IsGuardKey(key));
    }

    [Fact]
    public void EmptyCustomAllowlist_ErrorsOnSaveOnly()
    {
        var settings = DefaultFormSettings.Create() with { AllowedFieldIds = Array.Empty<Guid>() };

        Assert.Equal(new[] { FormSettingsValidator.EmptyAllowlistError }, FormSettingsValidator.ValidateForSave(settings));
        Assert.Empty(FormSettingsValidator.Validate(settings)); // Stored rows keep reading as before.
        Assert.Empty(FormSettingsValidator.ValidateForSave(settings with { AllowedFieldIds = null }));
    }

    private static FormGuardSettings WithQuestions(params QuestionSetting[] questions) =>
        DefaultFormSettings.Create() with { Questions = questions };

    private static void AssertQuestion(QuestionSetting q, string key, QuestionRole role, string text)
    {
        Assert.Equal(key, q.Key);
        Assert.Equal(role, q.Role);
        Assert.Equal(text, q.Text);
        Assert.True(q.Enabled);
    }
}

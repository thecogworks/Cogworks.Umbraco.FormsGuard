using Cogworks.Umbraco.FormsGuard.Decisions;

namespace Cogworks.Umbraco.FormsGuard.Settings;

/// <summary>The single source of shipped per-form defaults.</summary>
public static class DefaultFormSettings
{
    public static IReadOnlyList<QuestionSetting> Questions { get; } = new[]
    {
        new QuestionSetting(
            "guard.sales_pitch",
            "Is this an unsolicited offer to sell products or services to the organisation, such as SEO, web design, marketing, lead generation, staffing or software?",
            QuestionRole.SpamSignal,
            true,
            "The message offers to sell products or services to the organisation, such as SEO, web design, marketing, lead generation, staffing or software.",
            "The message does not offer to sell anything to the organisation; it asks for, or about, what the organisation provides."),
        new QuestionSetting(
            "guard.automated",
            "Does this look machine-generated or templated, such as nonsense text, keyword stuffing or placeholder text?",
            QuestionRole.SpamSignal,
            true,
            "The message looks machine-generated or templated, such as nonsense text, keyword stuffing or placeholder text.",
            "The message reads as written by a person about their own situation."),
        new QuestionSetting(
            "guard.phishing",
            "Does this message try to get the reader to click a link, download a file, or share login, payment or personal details?",
            QuestionRole.SpamSignal,
            true,
            "The message tries to get the reader to click a link, download a file, or share login, payment or personal details.",
            "The message does not ask the reader to click, download or share login, payment or personal details."),
        new QuestionSetting(
            "guard.generic",
            "Could this message have been sent unchanged to almost any organisation?",
            QuestionRole.Informational,
            true,
            "The message could be sent unchanged to almost any organisation.",
            "The message refers to this organisation or what it does specifically."),
        new QuestionSetting(
            "guard.genuine",
            "Is this a genuine enquiry or request from a person who wants something from the organisation described?",
            QuestionRole.GenuineSignal,
            true,
            "The message is a genuine enquiry or request from a person who wants something from the organisation described.",
            "The message is not a genuine enquiry to this organisation, such as spam, a sales pitch or an automated submission."),
    };

    public static DecisionThresholds Thresholds { get; } = new(
        QuarantineSpamMin: 0.85,
        ApproveSpamMax: 0.15,
        ApproveGenuineMin: 0.70);

    public static FormGuardSettings Create() => new(
        Organisation: string.Empty,
        AllowedFieldIds: null,
        SendEmailDomain: false,
        Questions: Questions,
        Thresholds: Thresholds,
        FailurePolicy: FailurePolicy.ApproveNotChecked,
        EmailFieldId: null);
}

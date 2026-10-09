using Cogworks.Umbraco.FormsGuard.Decisions;

namespace Cogworks.Umbraco.FormsGuard.Settings;

/// <summary>What happens to an entry when the provider keeps failing.</summary>
public enum FailurePolicy
{
    /// <summary>Approve the entry, marked "not checked".</summary>
    ApproveNotChecked,

    /// <summary>Leave the entry for human review.</summary>
    Review,
}

/// <summary>One per-form question as stored in settings.</summary>
/// <param name="Key">Namespaced key, e.g. <c>guard.sales_pitch</c>.</param>
/// <param name="Text">Administrator-editable question text.</param>
/// <param name="Role">How the answer feeds the decision rule.</param>
/// <param name="Enabled">Whether the question is asked.</param>
/// <param name="TrueCriteria">Optional wording of what makes the statement true; <c>null</c> omits it.</param>
/// <param name="FalseCriteria">Optional wording of what makes the statement false; <c>null</c> omits it.</param>
public sealed record QuestionSetting(
    string Key,
    string Text,
    QuestionRole Role,
    bool Enabled,
    string? TrueCriteria = null,
    string? FalseCriteria = null);

/// <summary>Probability thresholds for the decision rule.</summary>
/// <param name="QuarantineSpamMin">Quarantine when any spam signal is at or above this.</param>
/// <param name="ApproveSpamMax">Approve only when every spam signal is at or below this...</param>
/// <param name="ApproveGenuineMin">...and the genuine signal is at or above this.</param>
public sealed record DecisionThresholds(double QuarantineSpamMin, double ApproveSpamMax, double ApproveGenuineMin);

/// <summary>Per-form Forms Guard settings, stored as JSON in <c>cogFormsGuardFormSettings.Settings</c>.</summary>
/// <param name="Organisation">Administrator-written description of the organisation.</param>
/// <param name="AllowedFieldIds">Fields sent to the provider; <c>null</c> means the default allowlist rule.</param>
/// <param name="SendEmailDomain">Whether the submitter's email domain is sent.</param>
/// <param name="Questions">The form's guard questions.</param>
/// <param name="Thresholds">Decision rule thresholds.</param>
/// <param name="FailurePolicy">What happens after repeated provider failures.</param>
/// <param name="EmailFieldId">Field the email domain is read from; <c>null</c> means auto-detect.</param>
public sealed record FormGuardSettings(
    string Organisation,
    IReadOnlyList<Guid>? AllowedFieldIds,
    bool SendEmailDomain,
    IReadOnlyList<QuestionSetting> Questions,
    DecisionThresholds Thresholds,
    FailurePolicy FailurePolicy,
    Guid? EmailFieldId = null);

/// <summary>A form's guarded flag plus its effective settings.</summary>
public sealed record FormSettings(bool Guarded, FormGuardSettings Settings);

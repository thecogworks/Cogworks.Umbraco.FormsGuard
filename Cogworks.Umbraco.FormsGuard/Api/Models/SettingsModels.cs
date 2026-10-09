namespace Cogworks.Umbraco.FormsGuard.Api.Models;

/// <summary>A Forms form and whether Forms Guard checks its entries.</summary>
public sealed class FormSummary
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool Guarded { get; init; }

    /// <summary>Whether the Forms setup lets Forms Guard protect the form; null when the form is not guarded.</summary>
    public FormReadinessModel? Readiness { get; init; }
}

/// <summary>
/// Problems in a form's Forms setup that let spam through even when guarded. Workflow settings are never included.
/// </summary>
public sealed class FormReadinessModel
{
    /// <summary>True when Forms approves entries at once, so nothing waits for a decision.</summary>
    public bool ManualApprovalOff { get; init; }

    /// <summary>Names of active workflows that run on submit, before any decision.</summary>
    public IReadOnlyList<string> SubmitWorkflowNames { get; init; } = Array.Empty<string>();
}

/// <summary>A form's guarded flag, effective settings and fields (no field values).</summary>
public sealed class FormSettingsResponse
{
    public Guid FormId { get; init; }

    public string FormName { get; init; } = string.Empty;

    public bool Guarded { get; init; }

    public FormSettingsModel Settings { get; init; } = new();

    public IReadOnlyList<FormFieldModel> Fields { get; init; } = Array.Empty<FormFieldModel>();

    public FormReadinessModel Readiness { get; init; } = new();
}

/// <summary>
/// Per-form settings over the API. Members are nullable and enums are strings so that missing or unknown
/// values are reported as errors rather than defaulted.
/// </summary>
public sealed record FormSettingsModel
{
    public string? Organisation { get; init; }

    /// <summary>Fields sent to the provider; null means the default allowlist rule.</summary>
    public IReadOnlyList<Guid>? AllowedFieldIds { get; init; }

    public bool? SendEmailDomain { get; init; }

    public IReadOnlyList<QuestionModel?>? Questions { get; init; }

    public ThresholdsModel? Thresholds { get; init; }

    /// <summary><c>ApproveNotChecked</c> or <c>Review</c>.</summary>
    public string? FailurePolicy { get; init; }

    /// <summary>Field the email domain is read from; null means auto-detect.</summary>
    public Guid? EmailFieldId { get; init; }
}

public sealed record QuestionModel
{
    public string? Key { get; init; }

    public string? Text { get; init; }

    /// <summary><c>SpamSignal</c>, <c>GenuineSignal</c> or <c>Informational</c>.</summary>
    public string? Role { get; init; }

    public bool? Enabled { get; init; }

    public string? TrueCriteria { get; init; }

    public string? FalseCriteria { get; init; }
}

public sealed record ThresholdsModel
{
    public double? QuarantineSpamMin { get; init; }

    public double? ApproveSpamMax { get; init; }

    public double? ApproveGenuineMin { get; init; }
}

/// <summary>One Forms field's metadata. Never carries a submitted value.</summary>
public sealed class FormFieldModel
{
    public Guid Id { get; init; }

    public string? Caption { get; init; }

    public string? Alias { get; init; }

    /// <summary>True for upload and password fields, which are never sent or matched.</summary>
    public bool Excluded { get; init; }

    /// <summary>True when the field is an email candidate for auto-detection.</summary>
    public bool LooksLikeEmail { get; init; }
}

public sealed class SaveFormSettingsRequest
{
    public bool? Guarded { get; init; }

    public FormSettingsModel? Settings { get; init; }
}

/// <summary>A per-form hard rule.</summary>
public sealed class RuleModel
{
    public int Id { get; init; }

    /// <summary><c>BlockedDomain</c>, <c>AllowedDomain</c> or <c>BlockedPhrase</c>.</summary>
    public string RuleType { get; init; } = string.Empty;

    public string Pattern { get; init; } = string.Empty;

    public DateTime CreatedUtc { get; init; }
}

public sealed class CreateRuleRequest
{
    public string? RuleType { get; init; }

    public string? Pattern { get; init; }
}

/// <summary>Every problem found with the request; nothing was written.</summary>
public sealed class ValidationErrorsResponse
{
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

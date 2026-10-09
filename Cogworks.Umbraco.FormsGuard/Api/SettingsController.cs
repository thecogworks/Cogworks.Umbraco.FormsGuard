using System.Diagnostics.CodeAnalysis;
using Asp.Versioning;
using Cogworks.Umbraco.FormsGuard.Api.Models;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Cogworks.Umbraco.FormsGuard.Processing;
using Cogworks.Umbraco.FormsGuard.Security;
using Cogworks.Umbraco.FormsGuard.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Security;
using Umbraco.Forms.Core.Models;
using Umbraco.Forms.Core.Services;

namespace Cogworks.Umbraco.FormsGuard.Api;

[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Settings")]
public sealed class SettingsController : FormsGuardApiControllerBase
{
    private readonly ISettingsRepository _repository;
    private readonly IFormSettingsReader _settingsReader;
    private readonly IFormService _formService;
    private readonly IWorkflowService _workflowService;
    private readonly IFormAccess _formAccess;
    private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;

    public SettingsController(
        ISettingsRepository repository,
        IFormSettingsReader settingsReader,
        IFormService formService,
        IWorkflowService workflowService,
        IFormAccess formAccess,
        IBackOfficeSecurityAccessor backOfficeSecurityAccessor)
    {
        _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
        _repository = repository;
        _settingsReader = settingsReader;
        _formService = formService;
        _workflowService = workflowService;
        _formAccess = formAccess;
    }

    /// <summary>
    /// Lists the Forms forms the user can access with their guarded flag, sorted by name; guarded forms carry readiness.
    /// </summary>
    [HttpGet("settings/forms")]
    [Authorize(Policy = FormsGuardPermissions.ManageSettingsPolicy)]
    [ProducesResponseType<IReadOnlyList<FormSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IReadOnlyList<FormSummary> GetForms()
    {
        var allowed = _formAccess.SettingsFormIds();
        if (allowed.Count == 0)
        {
            return [];
        }

        var guarded = _repository.GetAllFormSettings().ToDictionary(r => r.FormId, r => r.Guarded);
        return _formService.GetSlim()
            .Where(f => allowed.Contains(f.Id))
            .Select(f =>
            {
                var isGuarded = guarded.TryGetValue(f.Id, out var g) && g;
                return new FormSummary
                {
                    Id = f.Id,
                    Name = f.Name ?? string.Empty,
                    Guarded = isGuarded,
                    Readiness = isGuarded ? ReadinessFor(f.Id) : null,
                };
            })
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Id)
            .ToList();
    }

    /// <summary>Reads a form's effective settings and its fields.</summary>
    [HttpGet("settings/forms/{formId:guid}")]
    [Authorize(Policy = FormsGuardPermissions.ManageSettingsPolicy)]
    [ProducesResponseType<FormSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetFormSettings(Guid formId)
    {
        var form = AccessibleForm(formId);
        return form is null ? NotFound() : Ok(BuildResponse(form));
    }

    /// <summary>Saves a form's guarded flag and settings after validation; 400 lists every problem.</summary>
    [HttpPut("settings/forms/{formId:guid}")]
    [Authorize(Policy = FormsGuardPermissions.ManageSettingsPolicy)]
    [ProducesResponseType<FormSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationErrorsResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult SaveFormSettings(Guid formId, SaveFormSettingsRequest request)
    {
        var form = AccessibleForm(formId);
        if (form is null)
        {
            return NotFound();
        }

        if (CurrentUserKey() is not { } actor)
        {
            return Unauthorized();
        }

        var errors = new List<string>();
        if (request.Guarded is null)
        {
            errors.Add("Guarded is required.");
        }

        var fieldIds = form.AllFields.Select(f => f.Id).ToHashSet();
        if (!TryMap(request.Settings, fieldIds, out var settings, out var mapErrors))
        {
            errors.AddRange(mapErrors);
        }

        if (errors.Count > 0 || settings is null)
        {
            return BadRequest(new ValidationErrorsResponse { Errors = errors });
        }

        _repository.SaveFormSettings(formId, request.Guarded!.Value, FormSettingsSerializer.Serialize(settings), actor);
        return Ok(BuildResponse(form));
    }

    /// <summary>Lists a form's hard rules in id order.</summary>
    [HttpGet("settings/forms/{formId:guid}/rules")]
    [Authorize(Policy = FormsGuardPermissions.ManageSettingsPolicy)]
    [ProducesResponseType<IReadOnlyList<RuleModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetRules(Guid formId)
    {
        if (AccessibleForm(formId) is null)
        {
            return NotFound();
        }

        return Ok(_repository.GetRules(formId).Select(ToRuleModel).ToList());
    }

    /// <summary>Adds a hard rule after validation; 400 lists every problem.</summary>
    [HttpPost("settings/forms/{formId:guid}/rules")]
    [Authorize(Policy = FormsGuardPermissions.ManageSettingsPolicy)]
    [ProducesResponseType<RuleModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationErrorsResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult CreateRule(Guid formId, CreateRuleRequest request)
    {
        if (AccessibleForm(formId) is null)
        {
            return NotFound();
        }

        if (CurrentUserKey() is not { } actor)
        {
            return Unauthorized();
        }

        var errors = RuleValidator.Validate(request.RuleType, request.Pattern, _repository.GetRules(formId));
        if (errors.Count > 0 || !HardRules.TryParse(request.RuleType, out var type))
        {
            return BadRequest(new ValidationErrorsResponse { Errors = errors });
        }

        var rule = new RuleDto
        {
            FormId = formId,
            RuleType = type.ToString(),
            Pattern = request.Pattern!.Trim(),
            CreatedUtc = DateTime.UtcNow,
        };
        _repository.InsertRule(rule, actor);
        return Ok(ToRuleModel(rule));
    }

    /// <summary>Deletes one of the form's hard rules.</summary>
    [HttpDelete("settings/forms/{formId:guid}/rules/{ruleId:int}")]
    [Authorize(Policy = FormsGuardPermissions.ManageSettingsPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult DeleteRule(Guid formId, int ruleId)
    {
        if (!_formAccess.CanAccessForm(formId))
        {
            return NotFound();
        }

        if (CurrentUserKey() is not { } actor)
        {
            return Unauthorized();
        }

        return _repository.DeleteRule(formId, ruleId, actor) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Maps the API model onto the domain settings. Rejects missing parts, unknown enum names and field ids that
    /// are not on the form, then runs <see cref="FormSettingsValidator.ValidateForSave"/>. Returns false with every error found.
    /// </summary>
    public static bool TryMap(
        FormSettingsModel? model,
        IReadOnlySet<Guid> formFieldIds,
        [NotNullWhen(true)] out FormGuardSettings? settings,
        out IReadOnlyList<string> errors)
    {
        settings = null;
        var list = new List<string>();
        errors = list;

        if (model is null)
        {
            list.Add("Settings are required.");
            return false;
        }

        if (model.Organisation is null)
        {
            list.Add("Organisation is required.");
        }

        if (model.SendEmailDomain is null)
        {
            list.Add("Send email domain is required.");
        }

        if (model.AllowedFieldIds is not null)
        {
            foreach (var id in model.AllowedFieldIds.Distinct().Where(id => !formFieldIds.Contains(id)))
            {
                list.Add($"Allowed field {id} is not on this form.");
            }
        }

        if (model.EmailFieldId is { } emailFieldId && !formFieldIds.Contains(emailFieldId))
        {
            list.Add($"Email field {emailFieldId} is not on this form.");
        }

        FailurePolicy? failurePolicy = null;
        if (model.FailurePolicy is null)
        {
            list.Add("Failure policy is required.");
        }
        else if (TryParseName<FailurePolicy>(model.FailurePolicy, out var parsedPolicy))
        {
            failurePolicy = parsedPolicy;
        }
        else
        {
            list.Add($"Failure policy '{model.FailurePolicy}' is not recognised; use ApproveNotChecked or Review.");
        }

        DecisionThresholds? thresholds = null;
        if (model.Thresholds is null)
        {
            list.Add("Thresholds are required.");
        }
        else
        {
            var t = model.Thresholds;
            if (t.QuarantineSpamMin is null)
            {
                list.Add("Quarantine spam minimum is required.");
            }

            if (t.ApproveSpamMax is null)
            {
                list.Add("Approve spam maximum is required.");
            }

            if (t.ApproveGenuineMin is null)
            {
                list.Add("Approve genuine minimum is required.");
            }

            // A missing part is replaced by the bound that cannot by itself break "approve maximum below
            // quarantine minimum", so the parts that were given are still checked without a spurious error.
            thresholds = new DecisionThresholds(
                t.QuarantineSpamMin ?? 1.0,
                t.ApproveSpamMax ?? 0.0,
                t.ApproveGenuineMin ?? DefaultFormSettings.Thresholds.ApproveGenuineMin);
        }

        IReadOnlyList<QuestionSetting> questions;
        if (model.Questions is null || model.Questions.Count == 0)
        {
            list.Add("At least one question is required.");
            questions = DefaultFormSettings.Questions;
        }
        else
        {
            questions = model.Questions
                .Select((q, i) => MapQuestion(q, i, list))
                .ToList();
        }

        // Always validate, so every problem is listed. Missing or unparseable parts take shipped defaults
        // (or a valid placeholder) here only so they add no second error; such a candidate is never returned.
        var defaults = DefaultFormSettings.Create();
        var candidate = new FormGuardSettings(
            Organisation: model.Organisation ?? defaults.Organisation,
            AllowedFieldIds: model.AllowedFieldIds?.Distinct().ToList(),
            SendEmailDomain: model.SendEmailDomain ?? defaults.SendEmailDomain,
            Questions: questions,
            Thresholds: thresholds ?? defaults.Thresholds,
            FailurePolicy: failurePolicy ?? defaults.FailurePolicy,
            EmailFieldId: model.EmailFieldId);

        list.AddRange(FormSettingsValidator.ValidateForSave(candidate));
        if (list.Count > 0)
        {
            return false;
        }

        settings = candidate;
        return true;
    }

    /// <summary>Maps domain settings to the API model.</summary>
    public static FormSettingsModel ToModel(FormGuardSettings settings) => new()
    {
        Organisation = settings.Organisation,
        AllowedFieldIds = settings.AllowedFieldIds?.ToList(),
        SendEmailDomain = settings.SendEmailDomain,
        Questions = settings.Questions
            .Select(q => (QuestionModel?)new QuestionModel
            {
                Key = q.Key,
                Text = q.Text,
                Role = q.Role.ToString(),
                Enabled = q.Enabled,
                TrueCriteria = q.TrueCriteria,
                FalseCriteria = q.FalseCriteria,
            })
            .ToList(),
        Thresholds = new ThresholdsModel
        {
            QuarantineSpamMin = settings.Thresholds.QuarantineSpamMin,
            ApproveSpamMax = settings.Thresholds.ApproveSpamMax,
            ApproveGenuineMin = settings.Thresholds.ApproveGenuineMin,
        },
        FailurePolicy = settings.FailurePolicy.ToString(),
        EmailFieldId = settings.EmailFieldId,
    };

    /// <summary>
    /// Maps one question, adding an error for each missing or unknown part. Missing parts are filled with valid
    /// placeholders so the validator still checks the rest without a second error for them.
    /// </summary>
    private static QuestionSetting MapQuestion(QuestionModel? model, int index, List<string> errors)
    {
        var label = $"Question {index + 1}";
        var placeholderKey = $"{QuestionKeys.GuardPrefix}missing_question_{index + 1}";
        if (model is null)
        {
            errors.Add($"{label} is missing.");
            return new QuestionSetting(placeholderKey, "Missing", QuestionRole.Informational, false);
        }

        if (model.Key is null)
        {
            errors.Add($"{label} key is required.");
        }

        if (model.Text is null)
        {
            errors.Add($"{label} text is required.");
        }

        if (model.Enabled is null)
        {
            errors.Add($"{label} enabled is required.");
        }

        QuestionRole role = default;
        if (model.Role is null)
        {
            errors.Add($"{label} role is required.");
        }
        else if (!TryParseName(model.Role, out role))
        {
            errors.Add($"{label} role '{model.Role}' is not recognised; use SpamSignal, GenuineSignal or Informational.");
        }

        return new QuestionSetting(
            model.Key ?? placeholderKey,
            model.Text ?? "Missing",
            role,
            model.Enabled ?? false,
            model.TrueCriteria,
            model.FalseCriteria);
    }

    /// <summary>Matches an enum member by name only (case-insensitive); numbers are never accepted.</summary>
    private static bool TryParseName<TEnum>(string value, out TEnum result)
        where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(candidate.ToString(), value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                result = candidate;
                return true;
            }
        }

        result = default;
        return false;
    }

    private FormSettingsResponse BuildResponse(Form form)
    {
        var current = _settingsReader.Get(form.Id);

        // A stale email override (field no longer on the form) means auto-detect, so the editor shows Auto.
        var settings = ToModel(current.Settings with
        {
            EmailFieldId = DecisionStateBuilder.ExistingFieldId(current.Settings.EmailFieldId, form.AllFields.Select(f => f.Id)),
        });

        return new FormSettingsResponse
        {
            FormId = form.Id,
            FormName = form.Name ?? string.Empty,
            Guarded = current.Guarded,
            Settings = settings,
            Fields = form.AllFields
                .Select(f => new FormFieldModel
                {
                    Id = f.Id,
                    Caption = f.Caption,
                    Alias = f.Alias,
                    Excluded = DecisionStateBuilder.IsExcludedType(f.FieldTypeId),
                    LooksLikeEmail = DecisionStateBuilder.LooksLikeEmail(f.Caption, f.Alias, f.RegEx, f.FieldTypeId),
                })
                .ToList(),
            Readiness = FormReadiness.Build(form, _workflowService),
        };
    }

    /// <summary>The authenticated backoffice user's key as the audit actor, never a value from the request.</summary>
    private string? CurrentUserKey() =>
        _backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser?.Key is { } key ? key.ToString() : null;

    /// <summary>The form, or null when it does not exist or the user cannot access it in Forms (both 404).</summary>
    private Form? AccessibleForm(Guid formId) =>
        _formAccess.CanAccessForm(formId) ? _formService.Get(formId) : null;

    /// <summary>FormSlim has no ManualApproval, so the full form is loaded for guarded ids only.</summary>
    private FormReadinessModel? ReadinessFor(Guid formId) =>
        _formService.Get(formId) is { } form ? FormReadiness.Build(form, _workflowService) : null;

    private static RuleModel ToRuleModel(RuleDto rule) => new()
    {
        Id = rule.Id,
        RuleType = rule.RuleType,
        Pattern = rule.Pattern,
        CreatedUtc = DecisionMapping.AsUtc(rule.CreatedUtc),
    };
}

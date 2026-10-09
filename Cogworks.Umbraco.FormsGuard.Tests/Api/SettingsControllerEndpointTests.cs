using System.Reflection;
using Cogworks.Umbraco.FormsGuard.Api;
using Cogworks.Umbraco.FormsGuard.Api.Models;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Cogworks.Umbraco.FormsGuard.Security;
using Cogworks.Umbraco.FormsGuard.Settings;
using Cogworks.Umbraco.FormsGuard.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Cms.Core.Security;
using Umbraco.Forms.Core.Models;
using Umbraco.Forms.Core.Services;
using FormsConstants = Umbraco.Forms.Core.Constants;
using FormsField = Umbraco.Forms.Core.Models.Field;
using FormsForm = Umbraco.Forms.Core.Models.Form;

namespace Cogworks.Umbraco.FormsGuard.Tests.Api;

/// <summary>Drives <see cref="SettingsController"/> over in-memory fakes, one test per I/O matrix row.</summary>
public class SettingsControllerEndpointTests
{
    private static readonly Guid Textfield = Guid.Parse(FormsConstants.FieldTypes.Textfield);
    private static readonly Guid Upload = Guid.Parse(FormsConstants.FieldTypes.Upload);
    private static readonly Guid AdminKey = Guid.NewGuid();

    private readonly FormsField _email = new() { Id = Guid.NewGuid(), Caption = "Email", Alias = "email", FieldTypeId = Textfield };
    private readonly FormsField _file = new() { Id = Guid.NewGuid(), Caption = "Attachment", Alias = "attachment", FieldTypeId = Upload };
    private readonly FormsForm _contact;
    private readonly FormsForm _apply;
    private readonly MemoryRepository _repo = new();
    private readonly SettingsController _controller;
    private readonly Dictionary<Guid, List<Workflow>> _submitWorkflows = new();
    private readonly IFormService _formService;

    public SettingsControllerEndpointTests()
    {
        _contact = Form("Contact", _email, _file);
        _apply = Form("apply");
        var forms = new[] { _contact, _apply };
        _formService = FormServiceFake.Create(forms);
        _controller = NewController(FormAccessFake.AllowAll(_contact.Id, _apply.Id));
    }

    private SettingsController NewController(IFormAccess access, IBackOfficeSecurityAccessor? user = null) => new(
        _repo,
        new FormSettingsReader(_repo, NullLogger<FormSettingsReader>.Instance),
        _formService,
        WorkflowServiceFake.Create(_submitWorkflows),
        access,
        user ?? SignedIn.Create(AdminKey));

    [Fact]
    public void ListForms_OnlyAccessibleForms()
    {
        var forms = NewController(FormAccessFake.AllowAll(_contact.Id)).GetForms();

        Assert.Equal(new[] { "Contact" }, forms.Select(f => f.Name));
    }

    [Fact]
    public void InaccessibleForm_EveryAction404_NothingWritten()
    {
        _repo.Rules.Add(new RuleDto { Id = 1, FormId = _apply.Id, RuleType = "BlockedDomain", Pattern = "a.example" });
        var controller = NewController(FormAccessFake.AllowAll(_contact.Id));
        var settings = SettingsController.ToModel(DefaultFormSettings.Create());

        Assert.IsType<NotFoundResult>(controller.GetFormSettings(_apply.Id));
        Assert.IsType<NotFoundResult>(
            controller.SaveFormSettings(_apply.Id, new SaveFormSettingsRequest { Guarded = true, Settings = settings }));
        Assert.IsType<NotFoundResult>(controller.GetRules(_apply.Id));
        Assert.IsType<NotFoundResult>(
            controller.CreateRule(_apply.Id, new CreateRuleRequest { RuleType = "BlockedDomain", Pattern = "b.example" }));
        Assert.IsType<NotFoundResult>(controller.DeleteRule(_apply.Id, 1));

        Assert.Empty(_repo.Settings);
        Assert.Equal(1, Assert.Single(_repo.Rules).Id);
        Assert.Empty(_repo.Audit);
    }

    [Fact]
    public void ListForms_SortedByName_NoRowMeansNotGuarded()
    {
        _repo.Settings[_apply.Id] = new FormSettingsDto { FormId = _apply.Id, Guarded = true, Settings = "{}" };

        var forms = _controller.GetForms();

        Assert.Equal(new[] { "apply", "Contact" }, forms.Select(f => f.Name));
        Assert.True(forms[0].Guarded);
        Assert.False(forms[1].Guarded);
    }

    [Fact]
    public void Readiness_GuardedOnlyInList_ReportsManualApprovalAndSubmitWorkflows()
    {
        _repo.Settings[_apply.Id] = new FormSettingsDto { FormId = _apply.Id, Guarded = true, Settings = "{}" };
        _apply.ManualApproval = false;
        _contact.ManualApproval = true;
        _submitWorkflows[_apply.Id] = [new Workflow { Name = "Notify team" }];

        var forms = _controller.GetForms();

        Assert.True(forms[0].Readiness!.ManualApprovalOff);
        Assert.Equal(new[] { "Notify team" }, forms[0].Readiness!.SubmitWorkflowNames);
        Assert.Null(forms[1].Readiness);

        var contact = Ok<FormSettingsResponse>(_controller.GetFormSettings(_contact.Id));
        Assert.False(contact.Readiness.ManualApprovalOff);
        Assert.Empty(contact.Readiness.SubmitWorkflowNames);

        _apply.ManualApproval = true;
        _submitWorkflows.Remove(_apply.Id);
        var fixedForm = _controller.GetForms()[0].Readiness!;
        Assert.False(fixedForm.ManualApprovalOff);
        Assert.Empty(fixedForm.SubmitWorkflowNames);
    }

    [Fact]
    public void Read_NoRow_IsUnguardedDefaultsWithFields()
    {
        var body = Ok<FormSettingsResponse>(_controller.GetFormSettings(_contact.Id));

        Assert.False(body.Guarded);
        Assert.Equal("Contact", body.FormName);
        Assert.Equal(
            FormSettingsSerializer.Serialize(DefaultFormSettings.Create()),
            Serialize(body.Settings));
        Assert.Collection(
            body.Fields,
            f => { Assert.Equal(_email.Id, f.Id); Assert.True(f.LooksLikeEmail); Assert.False(f.Excluded); },
            f => { Assert.Equal(_file.Id, f.Id); Assert.True(f.Excluded); Assert.False(f.LooksLikeEmail); });
    }

    [Fact]
    public void Read_UnknownForm_Is404()
    {
        Assert.IsType<NotFoundResult>(_controller.GetFormSettings(Guid.NewGuid()));
    }

    [Fact]
    public void SaveValid_UpsertsAndReturnsWhatAReadGives()
    {
        var settings = SettingsController.ToModel(DefaultFormSettings.Create()) with
        {
            Thresholds = new ThresholdsModel { QuarantineSpamMin = 0.5, ApproveSpamMax = 0.15, ApproveGenuineMin = 0.7 },
            EmailFieldId = _email.Id,
        };

        var body = Ok<FormSettingsResponse>(
            _controller.SaveFormSettings(_contact.Id, new SaveFormSettingsRequest { Guarded = true, Settings = settings }));

        var row = Assert.Single(_repo.Settings.Values);
        Assert.True(row.Guarded);
        Assert.True(row.UpdatedUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.True(body.Guarded);
        Assert.Equal(0.5, body.Settings.Thresholds!.QuarantineSpamMin);
        Assert.Equal(_email.Id, body.Settings.EmailFieldId);

        // The next decision reads the new threshold straight from the row (no cache).
        var read = Ok<FormSettingsResponse>(_controller.GetFormSettings(_contact.Id));
        Assert.Equal(Serialize(body.Settings), Serialize(read.Settings));

        // A second save updates the same row.
        _controller.SaveFormSettings(_contact.Id, new SaveFormSettingsRequest { Guarded = false, Settings = settings });
        Assert.False(Assert.Single(_repo.Settings.Values).Guarded);
    }

    [Fact]
    public void SaveInvalid_Is400WithEveryError_NothingWritten()
    {
        var settings = SettingsController.ToModel(DefaultFormSettings.Create()) with
        {
            Thresholds = new ThresholdsModel { QuarantineSpamMin = 1.5, ApproveSpamMax = 0.15, ApproveGenuineMin = 0.7 },
            FailurePolicy = "Reject",
            EmailFieldId = Guid.NewGuid(),
        };

        var result = Assert.IsType<BadRequestObjectResult>(
            _controller.SaveFormSettings(_contact.Id, new SaveFormSettingsRequest { Guarded = null, Settings = settings }));

        var body = Assert.IsType<ValidationErrorsResponse>(result.Value);
        // Guarded missing, unknown policy, email field not on the form, and the 1.5 threshold.
        Assert.Equal(4, body.Errors.Count);
        Assert.Contains("Quarantine spam minimum must be between 0 and 1.", body.Errors);
        Assert.Empty(_repo.Settings);
        Assert.Empty(_repo.Audit);
    }

    [Fact]
    public void SaveInvalid_ThresholdOutOfRange_Is400_NothingWritten()
    {
        var settings = SettingsController.ToModel(DefaultFormSettings.Create()) with
        {
            Thresholds = new ThresholdsModel { QuarantineSpamMin = 1.5, ApproveSpamMax = 0.15, ApproveGenuineMin = 0.7 },
        };

        Assert.IsType<BadRequestObjectResult>(
            _controller.SaveFormSettings(_contact.Id, new SaveFormSettingsRequest { Guarded = true, Settings = settings }));
        Assert.Empty(_repo.Settings);
    }

    [Fact]
    public void Save_UnknownForm_Is404_NothingWritten()
    {
        var settings = SettingsController.ToModel(DefaultFormSettings.Create());
        Assert.IsType<NotFoundResult>(
            _controller.SaveFormSettings(Guid.NewGuid(), new SaveFormSettingsRequest { Guarded = true, Settings = settings }));
        Assert.Empty(_repo.Settings);
    }

    [Fact]
    public void AddRule_StoresTrimmedPattern_AndLists()
    {
        var body = Ok<RuleModel>(
            _controller.CreateRule(_contact.Id, new CreateRuleRequest { RuleType = "blockeddomain", Pattern = "  @spam.example  " }));

        var stored = Assert.Single(_repo.Rules);
        Assert.Equal("@spam.example", stored.Pattern);
        Assert.Equal("BlockedDomain", stored.RuleType);
        Assert.Equal(stored.Id, body.Id);
        Assert.Equal("@spam.example", body.Pattern);
        Assert.Equal(DateTimeKind.Utc, body.CreatedUtc.Kind);

        var listed = Ok<List<RuleModel>>(_controller.GetRules(_contact.Id));
        Assert.Equal(body.Id, Assert.Single(listed).Id);
    }

    [Theory]
    [InlineData("Whitelist", "spam.example")]
    [InlineData("BlockedDomain", "   ")]
    [InlineData("BlockedDomain", "not a domain")]
    [InlineData("BlockedPhrase", "bad\u0001")]
    [InlineData("BlockedDomain", "SPAM.example")]
    public void BadRule_Is400_NothingWritten(string type, string pattern)
    {
        _repo.Rules.Add(new RuleDto { Id = 1, FormId = _contact.Id, RuleType = "BlockedDomain", Pattern = "@spam.example" });

        var result = Assert.IsType<BadRequestObjectResult>(
            _controller.CreateRule(_contact.Id, new CreateRuleRequest { RuleType = type, Pattern = pattern }));

        Assert.NotEmpty(Assert.IsType<ValidationErrorsResponse>(result.Value).Errors);
        Assert.Single(_repo.Rules);
    }

    [Fact]
    public void BadRule_FormAtLimit_Is400_NothingWritten()
    {
        for (var i = 1; i <= RuleValidator.MaxRulesPerForm; i++)
        {
            _repo.Rules.Add(new RuleDto { Id = i, FormId = _contact.Id, RuleType = "BlockedDomain", Pattern = $"d{i}.example" });
        }

        Assert.IsType<BadRequestObjectResult>(
            _controller.CreateRule(_contact.Id, new CreateRuleRequest { RuleType = "BlockedDomain", Pattern = "new.example" }));
        Assert.Equal(RuleValidator.MaxRulesPerForm, _repo.Rules.Count);
    }

    [Fact]
    public void DeleteRule_204_ThenMissingOrOtherForm_404()
    {
        _repo.Rules.Add(new RuleDto { Id = 1, FormId = _contact.Id, RuleType = "BlockedDomain", Pattern = "a.example" });
        _repo.Rules.Add(new RuleDto { Id = 2, FormId = _apply.Id, RuleType = "BlockedDomain", Pattern = "b.example" });

        Assert.IsType<NotFoundResult>(_controller.DeleteRule(_contact.Id, 2));
        Assert.Equal(2, _repo.Rules.Count);

        Assert.IsType<NoContentResult>(_controller.DeleteRule(_contact.Id, 1));
        Assert.Equal(2, Assert.Single(_repo.Rules).Id);

        Assert.IsType<NotFoundResult>(_controller.DeleteRule(_contact.Id, 1));
    }

    [Fact]
    public void Save_WritesSettingsAudit_ByUser_WithNoAdministratorText()
    {
        var settings = SettingsController.ToModel(DefaultFormSettings.Create()) with { Organisation = "Secret org text" };

        Ok<FormSettingsResponse>(
            _controller.SaveFormSettings(_contact.Id, new SaveFormSettingsRequest { Guarded = true, Settings = settings }));
        _controller.SaveFormSettings(_contact.Id, new SaveFormSettingsRequest { Guarded = false, Settings = settings });

        Assert.Collection(
            _repo.Audit,
            a => AssertAudit(a, SettingsRepository.SettingsSaveAction, "Guarded"),
            a => AssertAudit(a, SettingsRepository.SettingsSaveAction, "Not guarded"));
        Assert.DoesNotContain(_repo.Audit, a => a.Detail!.Contains("Secret", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateAndDeleteRule_WriteRuleAudit_WithoutPattern()
    {
        var created = Ok<RuleModel>(
            _controller.CreateRule(_contact.Id, new CreateRuleRequest { RuleType = "BlockedDomain", Pattern = "spam.example" }));
        Assert.IsType<NoContentResult>(_controller.DeleteRule(_contact.Id, created.Id));

        Assert.Collection(
            _repo.Audit,
            a => AssertAudit(a, SettingsRepository.RuleCreateAction, $"BlockedDomain rule {created.Id}"),
            a => AssertAudit(a, SettingsRepository.RuleDeleteAction, $"BlockedDomain rule {created.Id}"));
        Assert.DoesNotContain(_repo.Audit, a => a.Detail!.Contains("spam.example", StringComparison.Ordinal));
    }

    [Fact]
    public void BadRuleOrMissingDelete_WritesNoAudit()
    {
        Assert.IsType<BadRequestObjectResult>(
            _controller.CreateRule(_contact.Id, new CreateRuleRequest { RuleType = "BlockedDomain", Pattern = "not a domain" }));
        Assert.IsType<NotFoundResult>(_controller.DeleteRule(_contact.Id, 999));

        Assert.Empty(_repo.Audit);
    }

    [Fact]
    public void NoCurrentUser_EveryWrite401_NothingWritten()
    {
        _repo.Rules.Add(new RuleDto { Id = 1, FormId = _contact.Id, RuleType = "BlockedDomain", Pattern = "a.example" });
        var controller = NewController(FormAccessFake.AllowAll(_contact.Id), SignedIn.None());
        var settings = SettingsController.ToModel(DefaultFormSettings.Create());

        Assert.IsType<UnauthorizedResult>(
            controller.SaveFormSettings(_contact.Id, new SaveFormSettingsRequest { Guarded = true, Settings = settings }));
        Assert.IsType<UnauthorizedResult>(
            controller.CreateRule(_contact.Id, new CreateRuleRequest { RuleType = "BlockedDomain", Pattern = "b.example" }));
        Assert.IsType<UnauthorizedResult>(controller.DeleteRule(_contact.Id, 1));

        Assert.Empty(_repo.Settings);
        Assert.Equal(1, Assert.Single(_repo.Rules).Id);
        Assert.Empty(_repo.Audit);
    }

    [Fact]
    public void SaveEmptyCustomAllowlist_Is400_NothingWritten()
    {
        var settings = SettingsController.ToModel(DefaultFormSettings.Create()) with { AllowedFieldIds = [] };

        var result = Assert.IsType<BadRequestObjectResult>(
            _controller.SaveFormSettings(_contact.Id, new SaveFormSettingsRequest { Guarded = true, Settings = settings }));

        Assert.Contains(FormSettingsValidator.EmptyAllowlistError, Assert.IsType<ValidationErrorsResponse>(result.Value).Errors);
        Assert.Empty(_repo.Settings);
        Assert.Empty(_repo.Audit);
    }

    [Fact]
    public void Read_StaleEmailOverride_IsNull_AndSavesBack()
    {
        var stale = DefaultFormSettings.Create() with { EmailFieldId = Guid.NewGuid() };
        _repo.Settings[_contact.Id] = new FormSettingsDto
        {
            FormId = _contact.Id, Guarded = true, Settings = FormSettingsSerializer.Serialize(stale),
        };

        var body = Ok<FormSettingsResponse>(_controller.GetFormSettings(_contact.Id));
        Assert.Null(body.Settings.EmailFieldId);

        Ok<FormSettingsResponse>(
            _controller.SaveFormSettings(_contact.Id, new SaveFormSettingsRequest { Guarded = true, Settings = body.Settings }));
        var stored = FormSettingsSerializer.Parse(_repo.Settings[_contact.Id].Settings);
        Assert.True(stored.Valid);
        Assert.Null(stored.Settings.EmailFieldId);
    }

    private void AssertAudit(AuditDto audit, string action, string detail)
    {
        Assert.Equal(action, audit.Action);
        Assert.Equal(AdminKey.ToString(), audit.Actor);
        Assert.Equal(_contact.Id, audit.FormId);
        Assert.Null(audit.RecordId);
        Assert.Equal(detail, audit.Detail);
    }

    private static T Ok<T>(IActionResult result) =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(result).Value);

    private string Serialize(FormSettingsModel model)
    {
        Assert.True(SettingsController.TryMap(model, _contact.AllFields.Select(f => f.Id).ToHashSet(), out var settings, out _));
        return FormSettingsSerializer.Serialize(settings);
    }

    private static FormsForm Form(string name, params FormsField[] fields)
    {
        var container = new FieldsetContainer();
        container.Fields.AddRange(fields);
        var fieldSet = new FieldSet();
        fieldSet.Containers.Add(container);
        var page = new Page();
        page.FieldSets.Add(fieldSet);
        var form = new FormsForm { Id = Guid.NewGuid(), Name = name };
        form.Pages.Add(page);
        return form;
    }

    /// <summary>Answers <c>Get(Guid)</c> and <c>GetSlim()</c>; anything else fails the test.</summary>
    public class FormServiceFake : DispatchProxy
    {
        private IReadOnlyList<FormsForm> _forms = Array.Empty<FormsForm>();

        public static IFormService Create(IReadOnlyList<FormsForm> forms)
        {
            var proxy = DispatchProxy.Create<IFormService, FormServiceFake>();
            ((FormServiceFake)(object)proxy)._forms = forms;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var parameters = targetMethod?.GetParameters() ?? Array.Empty<ParameterInfo>();
            if (targetMethod?.Name == "Get" && parameters.Length == 1 && parameters[0].ParameterType == typeof(Guid))
            {
                return _forms.FirstOrDefault(f => f.Id == (Guid)args![0]!);
            }

            if (targetMethod?.Name == "GetSlim" && parameters.Length == 0)
            {
                return _forms.Select(f => new FormSlim { Id = f.Id, Name = f.Name }).ToList();
            }

            throw new NotSupportedException($"IFormService.{targetMethod?.Name} was not expected");
        }
    }

    /// <summary>Active on-submit workflows per form; anything else is unexpected (read only).</summary>
    public class WorkflowServiceFake : DispatchProxy
    {
        private Dictionary<Guid, List<Workflow>> _submit = new();

        public static IWorkflowService Create(Dictionary<Guid, List<Workflow>> submit)
        {
            var proxy = DispatchProxy.Create<IWorkflowService, WorkflowServiceFake>();
            ((WorkflowServiceFake)(object)proxy)._submit = submit;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "GetActiveWorkFlows"
                && args is [FormsForm form, global::Umbraco.Forms.Core.Enums.FormState state])
            {
                return state == global::Umbraco.Forms.Core.Enums.FormState.Submitted
                    && _submit.TryGetValue(form.Id, out var list) ? list : new List<Workflow>();
            }

            throw new NotSupportedException($"IWorkflowService.{targetMethod?.Name} was not expected");
        }
    }

    /// <summary>In-memory settings rows and rules.</summary>
    private sealed class MemoryRepository : ISettingsRepository
    {
        private int _nextRuleId = 1000;

        public Dictionary<Guid, FormSettingsDto> Settings { get; } = new();
        public List<RuleDto> Rules { get; } = new();
        public List<AuditDto> Audit { get; } = new();

        public FormSettingsDto? GetFormSettings(Guid formId) => Settings.GetValueOrDefault(formId);
        public IReadOnlyList<FormSettingsDto> GetAllFormSettings() => Settings.Values.ToList();

        public void SaveFormSettings(Guid formId, bool guarded, string settingsJson, string actor)
        {
            Settings[formId] = new FormSettingsDto { FormId = formId, Guarded = guarded, Settings = settingsJson, UpdatedUtc = DateTime.UtcNow };
            Audit.Add(SettingsRepository.SettingsSaveAudit(formId, guarded, actor, DateTime.UtcNow));
        }

        public IReadOnlyList<RuleDto> GetRules(Guid formId) => Rules.Where(r => r.FormId == formId).OrderBy(r => r.Id).ToList();

        public void InsertRule(RuleDto rule, string actor)
        {
            rule.Id = _nextRuleId++;
            Rules.Add(rule);
            Audit.Add(SettingsRepository.RuleAudit(SettingsRepository.RuleCreateAction, rule, actor, DateTime.UtcNow));
        }

        public bool DeleteRule(Guid formId, int ruleId, string actor)
        {
            var rule = Rules.FirstOrDefault(r => r.Id == ruleId && r.FormId == formId);
            if (rule is null)
            {
                return false;
            }

            Rules.Remove(rule);
            Audit.Add(SettingsRepository.RuleAudit(SettingsRepository.RuleDeleteAction, rule, actor, DateTime.UtcNow));
            return true;
        }
    }
}

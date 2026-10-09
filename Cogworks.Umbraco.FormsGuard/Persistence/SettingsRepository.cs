using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace Cogworks.Umbraco.FormsGuard.Persistence;

/// <summary>Reads and writes Forms Guard per-form settings and hard rules, auditing every write.</summary>
public interface ISettingsRepository
{
    /// <summary>The form's settings row, or null when it has none.</summary>
    FormSettingsDto? GetFormSettings(Guid formId);

    /// <summary>The form's hard rules, ordered by <c>Id</c>.</summary>
    IReadOnlyList<RuleDto> GetRules(Guid formId);

    /// <summary>Every per-form settings row.</summary>
    IReadOnlyList<FormSettingsDto> GetAllFormSettings();

    /// <summary>
    /// Inserts or updates the form's settings row, sets <c>UpdatedUtc</c> and writes a <c>settings-save</c> audit row by
    /// <paramref name="actor"/>, in one scope.
    /// </summary>
    void SaveFormSettings(Guid formId, bool guarded, string settingsJson, string actor);

    /// <summary>Inserts the rule, sets its <c>Id</c> and writes a <c>rule-create</c> audit row by <paramref name="actor"/>, in one scope.</summary>
    void InsertRule(RuleDto rule, string actor);

    /// <summary>
    /// Deletes the rule when it belongs to the form and writes a <c>rule-delete</c> audit row by <paramref name="actor"/>,
    /// in one scope. Returns whether a row was deleted; nothing is audited when none was.
    /// </summary>
    bool DeleteRule(Guid formId, int ruleId, string actor);
}

public sealed class SettingsRepository : ISettingsRepository
{
    /// <summary>The audit action for a settings save.</summary>
    public const string SettingsSaveAction = "settings-save";

    /// <summary>The audit action for a hard rule added.</summary>
    public const string RuleCreateAction = "rule-create";

    /// <summary>The audit action for a hard rule deleted.</summary>
    public const string RuleDeleteAction = "rule-delete";

    private readonly IScopeProvider _scopeProvider;

    public SettingsRepository(IScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public FormSettingsDto? GetFormSettings(Guid formId)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var sql = scope.SqlContext.Sql()
            .SelectAll()
            .From<FormSettingsDto>()
            .Where<FormSettingsDto>(x => x.FormId == formId);
        return scope.Database.FirstOrDefault<FormSettingsDto>(sql);
    }

    public IReadOnlyList<RuleDto> GetRules(Guid formId)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var sql = scope.SqlContext.Sql()
            .SelectAll()
            .From<RuleDto>()
            .Where<RuleDto>(x => x.FormId == formId)
            .OrderBy<RuleDto>(x => x.Id);
        return scope.Database.Fetch<RuleDto>(sql);
    }

    public IReadOnlyList<FormSettingsDto> GetAllFormSettings()
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var sql = scope.SqlContext.Sql()
            .SelectAll()
            .From<FormSettingsDto>();
        return scope.Database.Fetch<FormSettingsDto>(sql);
    }

    public void SaveFormSettings(Guid formId, bool guarded, string settingsJson, string actor)
    {
        using var scope = _scopeProvider.CreateScope();
        var existsSql = scope.SqlContext.Sql()
            .SelectCount()
            .From<FormSettingsDto>()
            .Where<FormSettingsDto>(x => x.FormId == formId);
        var row = new FormSettingsDto
        {
            FormId = formId,
            Guarded = guarded,
            Settings = settingsJson,
            UpdatedUtc = DateTime.UtcNow,
        };

        if (scope.Database.ExecuteScalar<int>(existsSql) > 0)
        {
            scope.Database.Update(row);
        }
        else
        {
            scope.Database.Insert(row);
        }

        scope.Database.Insert(SettingsSaveAudit(formId, guarded, actor, row.UpdatedUtc));
        scope.Complete();
    }

    public void InsertRule(RuleDto rule, string actor)
    {
        using var scope = _scopeProvider.CreateScope();
        scope.Database.Insert(rule);
        scope.Database.Insert(RuleAudit(RuleCreateAction, rule, actor, DateTime.UtcNow));
        scope.Complete();
    }

    public bool DeleteRule(Guid formId, int ruleId, string actor)
    {
        using var scope = _scopeProvider.CreateScope();
        var selectSql = scope.SqlContext.Sql()
            .SelectAll()
            .From<RuleDto>()
            .Where<RuleDto>(x => x.Id == ruleId && x.FormId == formId);
        var rule = scope.Database.FirstOrDefault<RuleDto>(selectSql);
        if (rule is null)
        {
            scope.Complete();
            return false;
        }

        var sql = scope.SqlContext.Sql()
            .Delete<RuleDto>()
            .Where<RuleDto>(x => x.Id == ruleId && x.FormId == formId);
        var deleted = scope.Database.Execute(sql) > 0;
        if (deleted)
        {
            scope.Database.Insert(RuleAudit(RuleDeleteAction, rule, actor, DateTime.UtcNow));
        }

        scope.Complete();
        return deleted;
    }

    /// <summary>The audit row for a settings save. The detail never holds administrator text.</summary>
    public static AuditDto SettingsSaveAudit(Guid formId, bool guarded, string actor, DateTime createdUtc) => new()
    {
        RecordId = null,
        FormId = formId,
        Action = SettingsSaveAction,
        Actor = actor,
        Detail = guarded ? "Guarded" : "Not guarded",
        CreatedUtc = createdUtc,
    };

    /// <summary>The audit row for a rule change: <c>"{RuleType} rule {Id}"</c>. The detail never holds the pattern.</summary>
    public static AuditDto RuleAudit(string action, RuleDto rule, string actor, DateTime createdUtc) => new()
    {
        RecordId = null,
        FormId = rule.FormId,
        Action = action,
        Actor = actor,
        Detail = $"{rule.RuleType} rule {rule.Id}",
        CreatedUtc = createdUtc,
    };
}

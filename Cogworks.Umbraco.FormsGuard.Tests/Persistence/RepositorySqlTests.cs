using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cogworks.Umbraco.FormsGuard.Tests.Persistence;

/// <summary>Runs the real repositories' SQL against in-memory SQLite, one test per I/O matrix row.</summary>
public sealed class RepositorySqlTests : IDisposable
{
    private readonly SqliteHarness _db = new();
    private readonly DecisionRepository _decisions;
    private readonly SettingsRepository _settings;
    private readonly Guid _formA = Guid.NewGuid();
    private readonly Guid _formB = Guid.NewGuid();
    private readonly DateTime _now = DateTime.UtcNow;

    public RepositorySqlTests()
    {
        _decisions = new DecisionRepository(_db.ScopeProvider, NullLogger<DecisionRepository>.Instance);
        _settings = new SettingsRepository(_db.ScopeProvider);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void TryReview_SecondCallFromSameStatus_ReturnsFalse_AndWritesNoAudit()
    {
        var row = AddDecision(_formA, DecisionStatus.Review);
        var stale = _decisions.GetByRecordId(row.RecordId)!;

        Assert.True(_decisions.TryReview(row, DecisionStatus.Review, DecisionStatus.Approved, "user-1", "review", out var auditId));
        Assert.True(auditId > 0);
        Assert.False(_decisions.TryReview(stale, DecisionStatus.Review, DecisionStatus.Approved, "user-2", "review", out var secondId));
        Assert.Equal(0, secondId);

        var stored = _decisions.GetByRecordId(row.RecordId)!;
        Assert.Equal("Approved", stored.Status);
        Assert.Equal("user-1", stored.Reviewer);
        var audit = Assert.Single(_decisions.GetAuditPage(0, 50).Rows);
        Assert.Equal(auditId, audit.Id);
    }

    [Fact]
    public void GetPage_StatusesAndFormIds_ReturnOnlyMatchingRows()
    {
        var review = AddDecision(_formA, DecisionStatus.Review);
        var quarantined = AddDecision(_formA, DecisionStatus.Quarantined);
        AddDecision(_formA, DecisionStatus.Approved);
        AddDecision(_formB, DecisionStatus.Review);
        AddDecision(_formB, DecisionStatus.Quarantined);
        var query = new DecisionQuery(["Review", "Quarantined"], null, null, null, [_formA]);

        var page = _decisions.GetPage(0, 50, query);

        Assert.Equal(2, page.Total);
        Assert.Equal(new[] { review.RecordId, quarantined.RecordId }.Order(), page.Rows.Select(r => r.RecordId).Order());
        Assert.Equal(2, _decisions.CountDecisions(query));
    }

    [Fact]
    public void GetPage_FormId_ReturnsOnlyThatFormsRows()
    {
        AddDecision(_formA, DecisionStatus.Review);
        AddDecision(_formA, DecisionStatus.Approved);
        var b1 = AddDecision(_formB, DecisionStatus.Review);
        var b2 = AddDecision(_formB, DecisionStatus.Approved);

        var page = _decisions.GetPage(0, 50, new DecisionQuery(null, _formB, null, null));

        Assert.Equal(2, page.Total);
        Assert.Equal(new[] { b1.RecordId, b2.RecordId }.Order(), page.Rows.Select(r => r.RecordId).Order());
    }

    [Fact]
    public void GetAuditPage_FormIds_ExcludesOtherFormsAndRowsWithNoForm()
    {
        _decisions.InsertSystemAudit(AddDecision(_formA, DecisionStatus.Approved), "decision", "a1");
        _decisions.InsertSystemAudit(AddDecision(_formA, DecisionStatus.Review), "decision", "a2");
        _decisions.InsertSystemAudit(AddDecision(_formB, DecisionStatus.Approved), "decision", "b1");
        _db.Database.Insert(new AuditDto { Action = "settings-save", Actor = "system", CreatedUtc = _now });

        var page = _decisions.GetAuditPage(0, 50, new AuditQuery(null, null, null, [_formA]));

        Assert.Equal(2, page.Total);
        Assert.All(page.Rows, r => Assert.Equal(_formA, r.FormId));
        Assert.Equal(4, _decisions.GetAuditPage(0, 50).Total);
    }

    [Fact]
    public void SettingsAndRules_RoundTrip_WithAudit()
    {
        _settings.SaveFormSettings(_formA, true, "{\"first\":1}", "admin");
        _settings.SaveFormSettings(_formA, false, "{\"second\":2}", "admin");
        var rule = new RuleDto { FormId = _formA, RuleType = "BlockedDomain", Pattern = "spam.example", CreatedUtc = _now };
        _settings.InsertRule(rule, "admin");

        Assert.True(rule.Id > 0);
        Assert.Single(_settings.GetRules(_formA));
        Assert.True(_settings.DeleteRule(_formA, rule.Id, "admin"));
        Assert.False(_settings.DeleteRule(_formA, rule.Id, "admin"));

        var stored = Assert.Single(_settings.GetAllFormSettings());
        Assert.False(stored.Guarded);
        Assert.Equal("{\"second\":2}", stored.Settings);
        Assert.Equal("{\"second\":2}", _settings.GetFormSettings(_formA)!.Settings);
        Assert.Empty(_settings.GetRules(_formA));

        var actions = _decisions.GetAuditPage(0, 50).Rows.Select(a => a.Action).Order().ToList();
        Assert.Equal(
            new[]
            {
                SettingsRepository.RuleCreateAction,
                SettingsRepository.RuleDeleteAction,
                SettingsRepository.SettingsSaveAction,
                SettingsRepository.SettingsSaveAction,
            },
            actions);
    }

    [Fact]
    public void GetQuarantinedBefore_ReturnsOnlyQuarantinedRowsLastChangedBeforeCutoff_InIdPages()
    {
        var cutoff = _now.AddDays(-30);
        var old1 = AddDecision(_formA, DecisionStatus.Quarantined, cutoff.AddMinutes(-1));
        AddDecision(_formA, DecisionStatus.Quarantined, cutoff.AddMinutes(1));
        AddDecision(_formA, DecisionStatus.Review, cutoff.AddDays(-5));
        var old2 = AddDecision(_formB, DecisionStatus.Quarantined, cutoff.AddDays(-5));
        var old3 = AddDecision(_formB, DecisionStatus.Quarantined, cutoff.AddDays(-10));

        Assert.Equal([old1.Id, old2.Id, old3.Id], _decisions.GetQuarantinedBefore(cutoff, 0, 50).Select(r => r.Id));
        Assert.Equal([old1.Id, old2.Id], _decisions.GetQuarantinedBefore(cutoff, 0, 2).Select(r => r.Id));
        Assert.Equal([old3.Id], _decisions.GetQuarantinedBefore(cutoff, old2.Id, 2).Select(r => r.Id));
    }

    private DecisionDto AddDecision(Guid formId, DecisionStatus status, DateTime? updatedUtc = null)
    {
        var row = new DecisionDto
        {
            RecordId = Guid.NewGuid(),
            FormId = formId,
            Status = status.ToString(),
            CreatedUtc = _now,
            UpdatedUtc = updatedUtc ?? _now,
        };
        _db.Database.Insert(row);
        return row;
    }
}

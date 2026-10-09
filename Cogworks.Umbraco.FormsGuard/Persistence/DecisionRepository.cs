using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Microsoft.Extensions.Logging;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;
using Record = Umbraco.Forms.Core.Persistence.Dtos.Record;

namespace Cogworks.Umbraco.FormsGuard.Persistence;

/// <summary>Reads and writes Forms Guard decision rows and audit rows.</summary>
public interface IDecisionRepository
{
    /// <summary>Inserts a <c>Pending</c> row for the record. Returns false if a row already exists.</summary>
    bool InsertPending(Guid recordId, Guid formId);

    /// <summary>
    /// Claims up to <paramref name="batchSize"/> due <c>Pending</c> rows that are unclaimed or were claimed
    /// before <paramref name="staleBefore"/>.
    /// </summary>
    IReadOnlyList<DecisionDto> ClaimPending(int batchSize, string claimant, DateTime staleBefore);

    /// <summary>Clears the claim so the row is due again. Leaves attempts and the next attempt time alone.</summary>
    void Unclaim(DecisionDto row);

    /// <summary>Records the decided outcome and clears the claim.</summary>
    void Complete(DecisionDto row, DecisionStatus status, string provider, string? modelVersion, string? probabilitiesJson);

    /// <summary>Records an outcome decided by a hard rule (no provider call) and clears the claim.</summary>
    void CompleteByRule(DecisionDto row, DecisionStatus status, string ruleHit);

    /// <summary>
    /// Records an outcome decided by the form's failure policy after a provider call was attempted, counts the attempt
    /// and clears the claim. The audit detail names <paramref name="reason"/>.
    /// </summary>
    void CompleteByPolicy(DecisionDto row, DecisionStatus status, string reason);

    /// <summary>
    /// Records an outcome decided without a provider call (for example <c>settings</c> or a kill switch before the call),
    /// leaves attempts unchanged and clears the claim. The audit detail is <c>"{Status} by {source}: {reason}"</c>.
    /// </summary>
    void CompleteWithoutCall(DecisionDto row, DecisionStatus status, string source, string reason);

    /// <summary>Records an outcome read back from the Forms record (already approved or rejected there) and clears the claim.</summary>
    void CompleteFromRecord(DecisionDto row, DecisionStatus status);

    /// <summary>Leaves the row <c>Pending</c>, increments attempts, schedules the next attempt and clears the claim.</summary>
    void Release(DecisionDto row, DateTime nextAttemptUtc);

    /// <summary>
    /// Deletes <paramref name="row"/> and, only when a row was deleted, writes a <c>system</c> audit row with
    /// <paramref name="action"/> and <paramref name="detail"/>, in one scope. Nothing is audited when the row was
    /// already gone (for example removed by the record-deleting handler).
    /// </summary>
    void DeleteWithAudit(DecisionDto row, string action, string detail);

    /// <summary>
    /// Up to <paramref name="take"/> <c>Quarantined</c> rows last updated before <paramref name="cutoffUtc"/> with an
    /// <c>Id</c> above <paramref name="afterId"/>, ordered by <c>Id</c>.
    /// </summary>
    IReadOnlyList<DecisionDto> GetQuarantinedBefore(DateTime cutoffUtc, int afterId, int take);

    /// <summary>Inserts a <c>system</c> audit row for <paramref name="row"/>.</summary>
    void InsertSystemAudit(DecisionDto row, string action, string detail);

    /// <summary>The decision row for the record, or null when it has none.</summary>
    DecisionDto? GetByRecordId(Guid recordId);

    /// <summary>
    /// Moves the row from <paramref name="from"/> to <paramref name="to"/> with <paramref name="reviewer"/> set, and inserts
    /// the audit row, in one scope. The update only applies while the row is still <paramref name="from"/>; returns false
    /// (and writes nothing) when it is not. On success the row object is updated and <paramref name="auditId"/> is set.
    /// </summary>
    bool TryReview(DecisionDto row, DecisionStatus from, DecisionStatus to, string reviewer, string auditAction, out int auditId);

    /// <summary>
    /// Undoes a <see cref="TryReview"/>: puts the row back from <paramref name="to"/> to <paramref name="from"/> with
    /// <paramref name="previousReviewer"/>, and deletes the audit row, in one scope.
    /// </summary>
    void RevertReview(DecisionDto row, DecisionStatus to, DecisionStatus from, string? previousReviewer, int auditId);

    /// <summary>
    /// A page of decisions, newest first (<c>CreatedUtc</c> then <c>Id</c> descending), and the total row count.
    /// When <paramref name="query"/> is given, only matching rows are read and counted.
    /// </summary>
    (IReadOnlyList<DecisionDto> Rows, long Total) GetPage(int skip, int take, DecisionQuery? query = null);

    /// <summary>Counts the decisions matching <paramref name="query"/>.</summary>
    long CountDecisions(DecisionQuery query);

    /// <summary>A page of audit rows, newest first (<c>CreatedUtc</c> then <c>Id</c> descending), and the total row count.</summary>
    (IReadOnlyList<AuditDto> Rows, long Total) GetAuditPage(int skip, int take, AuditQuery? query = null);

    /// <summary>
    /// Deletes the decision rows for <paramref name="recordIds"/> and writes one <c>record-deleted</c> <c>system</c> audit
    /// row per deleted row with <paramref name="detail"/>, in one scope. Existing audit rows are kept. Returns the number
    /// of rows deleted.
    /// </summary>
    int DeleteForRecords(IReadOnlyCollection<Guid> recordIds, string detail);

    /// <summary>
    /// Up to <paramref name="take"/> record ids of non-<c>Pending</c> decision rows whose Forms record (<c>UFRecords</c>)
    /// no longer exists.
    /// </summary>
    IReadOnlyList<Guid> GetOrphanRecordIds(int take);
}

public sealed class DecisionRepository : IDecisionRepository
{
    /// <summary>The audit actor for decisions the processor makes.</summary>
    public const string SystemActor = "system";

    /// <summary>The audit action for a decision row removed because its Forms record was deleted.</summary>
    public const string RecordDeletedAction = "record-deleted";

    /// <summary>The audit action for a quarantined Forms record deleted by the retention purge.</summary>
    public const string QuarantinePurgeAction = "quarantine-purge";

    /// <summary>The audit action for a decision row the processor removed while its Forms record remains.</summary>
    public const string DecisionRemovedAction = "decision-removed";

    /// <summary>Most ids per <c>IN</c> clause, well under SQL Server's parameter limit.</summary>
    private const int InChunkSize = 500;

    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<DecisionRepository> _logger;

    public DecisionRepository(IScopeProvider scopeProvider, ILogger<DecisionRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    public bool InsertPending(Guid recordId, Guid formId)
    {
        try
        {
            using var scope = _scopeProvider.CreateScope();
            var existsSql = scope.SqlContext.Sql()
                .SelectCount()
                .From<DecisionDto>()
                .Where<DecisionDto>(x => x.RecordId == recordId);
            if (scope.Database.ExecuteScalar<int>(existsSql) > 0)
            {
                scope.Complete();
                return false;
            }

            var now = DateTime.UtcNow;
            scope.Database.Insert(new DecisionDto
            {
                RecordId = recordId,
                FormId = formId,
                Status = DecisionStatus.Pending.ToString(),
                Attempts = 0,
                CreatedUtc = now,
                UpdatedUtc = now,
            });
            scope.Complete();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FormsGuard: failed to insert pending decision for record {RecordId} (form {FormId})", recordId, formId);
            return false;
        }
    }

    public IReadOnlyList<DecisionDto> ClaimPending(int batchSize, string claimant, DateTime staleBefore)
    {
        var claimed = new List<DecisionDto>();
        var pending = DecisionStatus.Pending.ToString();
        var now = DateTime.UtcNow;

        using var scope = _scopeProvider.CreateScope();

        var selectSql = scope.SqlContext.Sql()
            .SelectAll()
            .From<DecisionDto>()
            .Where<DecisionDto>(x => x.Status == pending)
            .Where<DecisionDto>(x => x.ClaimedBy == null || x.ClaimedUtc == null || x.ClaimedUtc < staleBefore)
            .Where<DecisionDto>(x => x.NextAttemptUtc == null || x.NextAttemptUtc <= now)
            .OrderBy<DecisionDto>(x => x.Id);
        selectSql = scope.SqlContext.SqlSyntax.SelectTop(selectSql, batchSize);
        var candidates = scope.Database.Fetch<DecisionDto>(selectSql);

        foreach (var row in candidates)
        {
            var updateSql = scope.SqlContext.Sql()
                .Update<DecisionDto>(u => u
                    .Set(x => x.ClaimedBy, claimant)
                    .Set(x => x.ClaimedUtc, now)
                    .Set(x => x.UpdatedUtc, now))
                .Where<DecisionDto>(x => x.Id == row.Id)
                .Where<DecisionDto>(x => x.Status == pending)
                .Where<DecisionDto>(x => x.ClaimedBy == null || x.ClaimedUtc == null || x.ClaimedUtc < staleBefore)
                .Where<DecisionDto>(x => x.NextAttemptUtc == null || x.NextAttemptUtc <= now);

            if (scope.Database.Execute(updateSql) == 1)
            {
                row.ClaimedBy = claimant;
                row.ClaimedUtc = now;
                row.UpdatedUtc = now;
                claimed.Add(row);
            }
        }

        scope.Complete();
        return claimed;
    }

    public void Complete(DecisionDto row, DecisionStatus status, string provider, string? modelVersion, string? probabilitiesJson) =>
        Finish(row, status, "provider", null, provider, modelVersion, probabilitiesJson, countAttempt: true);

    public void CompleteByRule(DecisionDto row, DecisionStatus status, string ruleHit) =>
        Finish(row, status, "rule", ruleHit, null, null, null, countAttempt: true);

    public void CompleteByPolicy(DecisionDto row, DecisionStatus status, string reason) =>
        Save(row, StampByPolicy(row, status, reason));

    public void CompleteWithoutCall(DecisionDto row, DecisionStatus status, string source, string reason) =>
        Save(row, StampWithoutCall(row, status, source, reason));

    /// <summary>Applies <see cref="CompleteByPolicy"/> to the row object and returns its audit row; no database access.</summary>
    public static AuditDto StampByPolicy(DecisionDto row, DecisionStatus status, string reason) =>
        Stamp(row, status, "policy", null, null, null, null, countAttempt: true, $"{status} by policy: {reason}");

    /// <summary>Applies <see cref="CompleteWithoutCall"/> to the row object and returns its audit row; no database access.</summary>
    public static AuditDto StampWithoutCall(DecisionDto row, DecisionStatus status, string source, string reason) =>
        Stamp(row, status, source, null, null, null, null, countAttempt: false, $"{status} by {source}: {reason}");

    public void CompleteFromRecord(DecisionDto row, DecisionStatus status) =>
        Finish(row, status, "record", null, null, null, null, countAttempt: true);

    public void Release(DecisionDto row, DateTime nextAttemptUtc)
    {
        row.Status = DecisionStatus.Pending.ToString();
        row.Attempts += 1;
        row.NextAttemptUtc = nextAttemptUtc;
        row.ClaimedBy = null;
        row.ClaimedUtc = null;
        row.UpdatedUtc = DateTime.UtcNow;
        Save(row);
    }

    public void Unclaim(DecisionDto row)
    {
        var now = DateTime.UtcNow;
        using var scope = _scopeProvider.CreateScope();
        var sql = scope.SqlContext.Sql()
            .Update<DecisionDto>(u => u
                .Set(x => x.ClaimedBy, null)
                .Set(x => x.ClaimedUtc, null)
                .Set(x => x.UpdatedUtc, now))
            .Where<DecisionDto>(x => x.Id == row.Id);
        scope.Database.Execute(sql);
        scope.Complete();

        row.ClaimedBy = null;
        row.ClaimedUtc = null;
        row.UpdatedUtc = now;
    }

    public void DeleteWithAudit(DecisionDto row, string action, string detail)
    {
        using var scope = _scopeProvider.CreateScope();
        if (scope.Database.Delete<DecisionDto>(row.Id) > 0)
        {
            scope.Database.Insert(NewAudit(row, action, SystemActor, detail, DateTime.UtcNow));
        }

        scope.Complete();
    }

    public IReadOnlyList<DecisionDto> GetQuarantinedBefore(DateTime cutoffUtc, int afterId, int take)
    {
        var quarantined = DecisionStatus.Quarantined.ToString();
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var sql = scope.SqlContext.Sql()
            .SelectAll()
            .From<DecisionDto>()
            .Where<DecisionDto>(x => x.Status == quarantined && x.UpdatedUtc < cutoffUtc && x.Id > afterId)
            .OrderBy<DecisionDto>(x => x.Id);
        sql = scope.SqlContext.SqlSyntax.SelectTop(sql, Math.Max(1, take));
        return scope.Database.Fetch<DecisionDto>(sql);
    }

    public void InsertSystemAudit(DecisionDto row, string action, string detail)
    {
        using var scope = _scopeProvider.CreateScope();
        scope.Database.Insert(NewAudit(row, action, SystemActor, detail, DateTime.UtcNow));
        scope.Complete();
    }

    public (IReadOnlyList<DecisionDto> Rows, long Total) GetPage(int skip, int take, DecisionQuery? query = null)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var countSql = FilterDecisions(scope.SqlContext.Sql().SelectCount().From<DecisionDto>(), query);
        var total = scope.Database.ExecuteScalar<long>(countSql);

        var pageSql = FilterDecisions(scope.SqlContext.Sql().SelectAll().From<DecisionDto>(), query)
            .OrderByDescending<DecisionDto>(x => x.CreatedUtc, x => x.Id);
        var rows = scope.Database.SkipTake<DecisionDto>(skip, take, pageSql);
        return (rows, total);
    }

    public long CountDecisions(DecisionQuery query)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var sql = FilterDecisions(scope.SqlContext.Sql().SelectCount().From<DecisionDto>(), query);
        return scope.Database.ExecuteScalar<long>(sql);
    }

    public (IReadOnlyList<AuditDto> Rows, long Total) GetAuditPage(int skip, int take, AuditQuery? query = null)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var countSql = FilterAudit(scope.SqlContext.Sql().SelectCount().From<AuditDto>(), query);
        var total = scope.Database.ExecuteScalar<long>(countSql);

        var pageSql = FilterAudit(scope.SqlContext.Sql().SelectAll().From<AuditDto>(), query)
            .OrderByDescending<AuditDto>(x => x.CreatedUtc, x => x.Id);
        var rows = scope.Database.SkipTake<AuditDto>(skip, take, pageSql);
        return (rows, total);
    }

    public int DeleteForRecords(IReadOnlyCollection<Guid> recordIds, string detail)
    {
        var ids = recordIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        var deleted = 0;
        var now = DateTime.UtcNow;

        // Always completed, even on failure: inside Forms' delete this scope is nested, and an uncompleted one would
        // roll Forms back. Each chunk writes its audit rows before its DELETE, so a failure can at worst commit an audit
        // row for a decision row that still exists; the orphan sweep (or a later delete) removes that row and audits again.
        using var scope = _scopeProvider.CreateScope();
        try
        {
            deleted = DeleteChunks(scope, ids, detail, now);
        }
        finally
        {
            scope.Complete();
        }

        return deleted;
    }

    private static int DeleteChunks(IScope scope, List<Guid> ids, string detail, DateTime now)
    {
        var deleted = 0;
        foreach (var chunk in ids.Chunk(InChunkSize))
        {
            var selectSql = scope.SqlContext.Sql()
                .SelectAll()
                .From<DecisionDto>()
                .WhereIn<DecisionDto>(x => x.RecordId, chunk);
            var rows = scope.Database.Fetch<DecisionDto>(selectSql);
            if (rows.Count == 0)
            {
                continue;
            }

            // Audit first, then delete, so a failure never leaves a deleted row without its audit row.
            foreach (var row in rows)
            {
                scope.Database.Insert(NewAudit(row, RecordDeletedAction, SystemActor, detail, now));
            }

            var rowIds = rows.Select(x => x.Id).ToArray();
            var deleteSql = scope.SqlContext.Sql()
                .Delete<DecisionDto>()
                .WhereIn<DecisionDto>(x => x.Id, rowIds);
            scope.Database.Execute(deleteSql);

            deleted += rows.Count;
        }

        return deleted;
    }

    public IReadOnlyList<Guid> GetOrphanRecordIds(int take)
    {
        var pending = DecisionStatus.Pending.ToString();
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var sql = scope.SqlContext.Sql()
            .Select<DecisionDto>(x => x.RecordId)
            .From<DecisionDto>()
            .LeftJoin<Record>().On<DecisionDto, Record>((d, r) => d.RecordId == r.UniqueId)
            .WhereNull<Record>(x => x.UniqueId)
            .Where<DecisionDto>(x => x.Status != pending)
            .OrderBy<DecisionDto>(x => x.Id);
        sql = scope.SqlContext.SqlSyntax.SelectTop(sql, Math.Max(1, take));
        return scope.Database.Fetch<Guid>(sql);
    }

    private static Sql<ISqlContext> FilterDecisions(Sql<ISqlContext> sql, DecisionQuery? query)
    {
        if (query is null)
        {
            return sql;
        }

        if (query.Statuses is not null)
        {
            sql = sql.WhereIn<DecisionDto>(x => x.Status, query.Statuses);
        }

        if (query.FormId is { } formId)
        {
            sql = sql.Where<DecisionDto>(x => x.FormId == formId);
        }

        if (query.FormIds is not null)
        {
            sql = sql.WhereIn<DecisionDto>(x => x.FormId, query.FormIds);
        }

        if (query.FromUtc is { } fromUtc)
        {
            sql = sql.Where<DecisionDto>(x => x.CreatedUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            sql = sql.Where<DecisionDto>(x => x.CreatedUtc < toUtc);
        }

        return sql;
    }

    private static Sql<ISqlContext> FilterAudit(Sql<ISqlContext> sql, AuditQuery? query)
    {
        if (query is null)
        {
            return sql;
        }

        if (query.FormId is { } formId)
        {
            sql = sql.Where<AuditDto>(x => x.FormId == formId);
        }

        // IN never matches NULL, so audit rows with no form drop out here.
        if (query.FormIds is not null)
        {
            sql = sql.WhereIn<AuditDto>(x => x.FormId, query.FormIds);
        }

        if (query.FromUtc is { } fromUtc)
        {
            sql = sql.Where<AuditDto>(x => x.CreatedUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            sql = sql.Where<AuditDto>(x => x.CreatedUtc < toUtc);
        }

        return sql;
    }

    private void Finish(
        DecisionDto row,
        DecisionStatus status,
        string source,
        string? ruleHit,
        string? provider,
        string? modelVersion,
        string? probabilitiesJson,
        bool countAttempt) =>
        Save(row, Stamp(row, status, source, ruleHit, provider, modelVersion, probabilitiesJson, countAttempt, null));

    /// <summary>Records the outcome on the row object, clears the claim and returns the <c>system</c> audit row.</summary>
    private static AuditDto Stamp(
        DecisionDto row,
        DecisionStatus status,
        string source,
        string? ruleHit,
        string? provider,
        string? modelVersion,
        string? probabilitiesJson,
        bool countAttempt,
        string? detail)
    {
        row.Status = status.ToString();
        row.Source = source;
        row.RuleHit = ruleHit;
        row.Provider = provider;
        row.ModelVersion = modelVersion;
        row.Probabilities = probabilitiesJson;
        if (countAttempt)
        {
            row.Attempts += 1;
        }

        row.NextAttemptUtc = null;
        row.ClaimedBy = null;
        row.ClaimedUtc = null;
        row.UpdatedUtc = DateTime.UtcNow;
        return NewAudit(row, "decision", SystemActor, detail ?? $"{row.Status} by {source}", row.UpdatedUtc);
    }

    public DecisionDto? GetByRecordId(Guid recordId)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        var sql = scope.SqlContext.Sql()
            .SelectAll()
            .From<DecisionDto>()
            .Where<DecisionDto>(x => x.RecordId == recordId);
        return scope.Database.FirstOrDefault<DecisionDto>(sql);
    }

    public bool TryReview(DecisionDto row, DecisionStatus from, DecisionStatus to, string reviewer, string auditAction, out int auditId)
    {
        auditId = 0;
        var fromText = from.ToString();
        var toText = to.ToString();
        var now = DateTime.UtcNow;

        using var scope = _scopeProvider.CreateScope();
        var sql = scope.SqlContext.Sql()
            .Update<DecisionDto>(u => u
                .Set(x => x.Status, toText)
                .Set(x => x.Reviewer, reviewer)
                .Set(x => x.UpdatedUtc, now))
            .Where<DecisionDto>(x => x.Id == row.Id)
            .Where<DecisionDto>(x => x.Status == fromText);
        if (scope.Database.Execute(sql) != 1)
        {
            // Nothing changed; complete the empty scope so an ambient caller's scope is not rolled back.
            scope.Complete();
            return false;
        }

        var audit = NewAudit(row, auditAction, reviewer, $"{fromText} to {toText}", now);
        scope.Database.Insert(audit);
        scope.Complete();

        auditId = audit.Id;
        row.Status = toText;
        row.Reviewer = reviewer;
        row.UpdatedUtc = now;
        return true;
    }

    public void RevertReview(DecisionDto row, DecisionStatus to, DecisionStatus from, string? previousReviewer, int auditId)
    {
        var fromText = from.ToString();
        var toText = to.ToString();
        var now = DateTime.UtcNow;

        using var scope = _scopeProvider.CreateScope();
        var sql = scope.SqlContext.Sql()
            .Update<DecisionDto>(u => u
                .Set(x => x.Status, fromText)
                .Set(x => x.Reviewer, previousReviewer)
                .Set(x => x.UpdatedUtc, now))
            .Where<DecisionDto>(x => x.Id == row.Id)
            .Where<DecisionDto>(x => x.Status == toText);
        scope.Database.Execute(sql);
        scope.Database.Delete<AuditDto>(auditId);
        scope.Complete();

        row.Status = fromText;
        row.Reviewer = previousReviewer;
        row.UpdatedUtc = now;
    }

    private void Save(DecisionDto row, AuditDto? audit = null)
    {
        using var scope = _scopeProvider.CreateScope();
        scope.Database.Update(row);
        if (audit is not null)
        {
            scope.Database.Insert(audit);
        }

        scope.Complete();
    }

    private static AuditDto NewAudit(DecisionDto row, string action, string actor, string detail, DateTime createdUtc) => new()
    {
        RecordId = row.RecordId,
        FormId = row.FormId,
        Action = action,
        Actor = actor,
        Detail = detail,
        CreatedUtc = createdUtc,
    };
}

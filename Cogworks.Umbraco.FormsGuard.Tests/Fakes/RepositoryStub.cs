using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;

namespace Cogworks.Umbraco.FormsGuard.Tests.Fakes;

/// <summary>Every <see cref="IDecisionRepository"/> member throws; fakes override only the members they use.</summary>
public abstract class RepositoryStub : IDecisionRepository
{
    public virtual bool InsertPending(Guid recordId, Guid formId) => throw new NotSupportedException();
    public virtual IReadOnlyList<DecisionDto> ClaimPending(int batchSize, string claimant, DateTime staleBefore) => throw new NotSupportedException();
    public virtual void Unclaim(DecisionDto row) => throw new NotSupportedException();
    public virtual void Complete(DecisionDto row, DecisionStatus status, string provider, string? modelVersion, string? probabilitiesJson) => throw new NotSupportedException();
    public virtual void CompleteByRule(DecisionDto row, DecisionStatus status, string ruleHit) => throw new NotSupportedException();
    public virtual void CompleteByPolicy(DecisionDto row, DecisionStatus status, string reason) => throw new NotSupportedException();
    public virtual void CompleteWithoutCall(DecisionDto row, DecisionStatus status, string source, string reason) => throw new NotSupportedException();
    public virtual void CompleteFromRecord(DecisionDto row, DecisionStatus status) => throw new NotSupportedException();
    public virtual void Release(DecisionDto row, DateTime nextAttemptUtc) => throw new NotSupportedException();
    public virtual void DeleteWithAudit(DecisionDto row, string action, string detail) => throw new NotSupportedException();
    public virtual IReadOnlyList<DecisionDto> GetQuarantinedBefore(DateTime cutoffUtc, int afterId, int take) => throw new NotSupportedException();
    public virtual void InsertSystemAudit(DecisionDto row, string action, string detail) => throw new NotSupportedException();
    public virtual DecisionDto? GetByRecordId(Guid recordId) => throw new NotSupportedException();
    public virtual bool TryReview(DecisionDto row, DecisionStatus from, DecisionStatus to, string reviewer, string auditAction, out int auditId) => throw new NotSupportedException();
    public virtual void RevertReview(DecisionDto row, DecisionStatus to, DecisionStatus from, string? previousReviewer, int auditId) => throw new NotSupportedException();
    public virtual (IReadOnlyList<DecisionDto> Rows, long Total) GetPage(int skip, int take, DecisionQuery? query = null) => throw new NotSupportedException();
    public virtual long CountDecisions(DecisionQuery query) => throw new NotSupportedException();
    public virtual (IReadOnlyList<AuditDto> Rows, long Total) GetAuditPage(int skip, int take, AuditQuery? query = null) => throw new NotSupportedException();
    public virtual int DeleteForRecords(IReadOnlyCollection<Guid> recordIds, string detail) => throw new NotSupportedException();
    public virtual IReadOnlyList<Guid> GetOrphanRecordIds(int take) => throw new NotSupportedException();
}

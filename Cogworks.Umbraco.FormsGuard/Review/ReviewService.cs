using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Processing;
using Microsoft.Extensions.Logging;
using Umbraco.Forms.Core.Data.Storage;
using Umbraco.Forms.Core.Services;

namespace Cogworks.Umbraco.FormsGuard.Review;

/// <summary>How a review action ended.</summary>
public enum ReviewOutcome
{
    /// <summary>The row moved, the record was updated and the audit row written.</summary>
    Done,

    /// <summary>No decision row, or its form or record is gone. Nothing written.</summary>
    NotFound,

    /// <summary>The action is not allowed from the row's status, or another reviewer got there first. Nothing written.</summary>
    Refused,

    /// <summary>The Forms record call failed; the row and audit were put back.</summary>
    Failed,
}

/// <summary>The result of a review action and the row's status afterwards (null when unknown).</summary>
public sealed record ReviewResult(ReviewOutcome Outcome, DecisionStatus? Status);

/// <summary>Reviewer actions on a decision: approve or confirm spam from Review, restore from Quarantined.</summary>
public interface IReviewService
{
    /// <summary>Review to Approved; approves the Forms record.</summary>
    Task<ReviewResult> ApproveAsync(Guid recordId, Guid reviewerKey, CancellationToken cancellationToken);

    /// <summary>Review to Quarantined; rejects the Forms record.</summary>
    Task<ReviewResult> ConfirmSpamAsync(Guid recordId, Guid reviewerKey, CancellationToken cancellationToken);

    /// <summary>Quarantined to Approved; approves the Forms record, so its on-approve workflows run.</summary>
    Task<ReviewResult> RestoreAsync(Guid recordId, Guid reviewerKey, CancellationToken cancellationToken);
}

public sealed class ReviewService : IReviewService
{
    private readonly IDecisionRepository _repository;
    private readonly IFormService _formService;
    private readonly IRecordStorage _recordStorage;
    private readonly IOutcomeApplier _applier;
    private readonly ILogger<ReviewService> _logger;

    public ReviewService(
        IDecisionRepository repository,
        IFormService formService,
        IRecordStorage recordStorage,
        IOutcomeApplier applier,
        ILogger<ReviewService> logger)
    {
        _repository = repository;
        _formService = formService;
        _recordStorage = recordStorage;
        _applier = applier;
        _logger = logger;
    }

    public Task<ReviewResult> ApproveAsync(Guid recordId, Guid reviewerKey, CancellationToken cancellationToken) =>
        ReviewAsync(ReviewAction.Approve, recordId, reviewerKey, cancellationToken);

    public Task<ReviewResult> ConfirmSpamAsync(Guid recordId, Guid reviewerKey, CancellationToken cancellationToken) =>
        ReviewAsync(ReviewAction.ConfirmSpam, recordId, reviewerKey, cancellationToken);

    public Task<ReviewResult> RestoreAsync(Guid recordId, Guid reviewerKey, CancellationToken cancellationToken) =>
        ReviewAsync(ReviewAction.Restore, recordId, reviewerKey, cancellationToken);

    private async Task<ReviewResult> ReviewAsync(
        ReviewAction action, Guid recordId, Guid reviewerKey, CancellationToken cancellationToken)
    {
        var row = _repository.GetByRecordId(recordId);
        if (row is null)
        {
            return new ReviewResult(ReviewOutcome.NotFound, null);
        }

        if (!Enum.TryParse<DecisionStatus>(row.Status, out var from))
        {
            _logger.LogWarning(
                "FormsGuard: decision for record {RecordId} (form {FormId}) has unknown status {Status}; {Action} refused",
                row.RecordId, row.FormId, row.Status, ReviewTransition.AuditAction(action));
            return new ReviewResult(ReviewOutcome.Refused, null);
        }

        if (ReviewTransition.Target(action, from) is not { } target)
        {
            return new ReviewResult(ReviewOutcome.Refused, from);
        }

        var form = _formService.Get(row.FormId);
        var record = form is null ? null : _recordStorage.GetRecordByUniqueId(row.RecordId, form);
        if (form is null || record is null)
        {
            return new ReviewResult(ReviewOutcome.NotFound, from);
        }

        var reviewer = reviewerKey.ToString();
        var previousReviewer = row.Reviewer;
        var auditAction = ReviewTransition.AuditAction(action);

        // The conditional update is the lock: a second reviewer is refused here, before any Forms call or email.
        if (!_repository.TryReview(row, from, target, reviewer, auditAction, out var auditId))
        {
            var current = _repository.GetByRecordId(recordId);
            return new ReviewResult(
                ReviewOutcome.Refused,
                current is not null && Enum.TryParse<DecisionStatus>(current.Status, out var now) ? now : null);
        }

        if (ReviewTransition.NeedsRecordCall(target, record.State))
        {
            try
            {
                await _applier.ApplyToRecordAsync(target, record, form);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex, "FormsGuard: {Action} failed on the Forms record {RecordId} (form {FormId}); reverting the decision to {Status}",
                    auditAction, row.RecordId, row.FormId, from);
                try
                {
                    _repository.RevertReview(row, target, from, previousReviewer, auditId);
                }
                catch (Exception revertEx)
                {
                    _logger.LogError(
                        revertEx, "FormsGuard: could not revert the decision for record {RecordId} (form {FormId}) after a failed {Action}",
                        row.RecordId, row.FormId, auditAction);
                    return new ReviewResult(ReviewOutcome.Failed, null);
                }

                return new ReviewResult(ReviewOutcome.Failed, from);
            }
        }

        _logger.LogInformation(
            "FormsGuard: record {RecordId} (form {FormId}) {Action} by reviewer {Reviewer}: {From} to {To}",
            row.RecordId, row.FormId, auditAction, reviewer, from, target);

        // The review is committed; a request cancelled from here on must not cancel the handlers.
        await _applier.RunHandlersAsync(
            new DecisionOutcome(row.RecordId, row.FormId, target, Array.Empty<DecisionAnswer>()), CancellationToken.None);

        return new ReviewResult(ReviewOutcome.Done, target);
    }
}

using Asp.Versioning;
using Cogworks.Umbraco.FormsGuard.Api.Models;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Cogworks.Umbraco.FormsGuard.Processing;
using Cogworks.Umbraco.FormsGuard.Review;
using Cogworks.Umbraco.FormsGuard.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Security;
using Umbraco.Forms.Core.Data.Storage;
using Umbraco.Forms.Core.Models;
using Umbraco.Forms.Core.Services;

namespace Cogworks.Umbraco.FormsGuard.Api;

[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Review")]
public sealed class ReviewController : FormsGuardApiControllerBase
{
    /// <summary>The statuses a reviewer can act on.</summary>
    public static readonly IReadOnlyCollection<string> QueueStatuses =
        [nameof(DecisionStatus.Review), nameof(DecisionStatus.Quarantined)];

    private readonly IDecisionRepository _repository;
    private readonly IReviewService _reviewService;
    private readonly IFormService _formService;
    private readonly IRecordStorage _recordStorage;
    private readonly IBackOfficeSecurityAccessor _backOfficeSecurityAccessor;
    private readonly IFormAccess _formAccess;
    private readonly ILogger<ReviewController> _logger;

    public ReviewController(
        IDecisionRepository repository,
        IReviewService reviewService,
        IFormService formService,
        IRecordStorage recordStorage,
        IBackOfficeSecurityAccessor backOfficeSecurityAccessor,
        IFormAccess formAccess,
        ILogger<ReviewController> logger)
    {
        _repository = repository;
        _reviewService = reviewService;
        _formService = formService;
        _recordStorage = recordStorage;
        _backOfficeSecurityAccessor = backOfficeSecurityAccessor;
        _formAccess = formAccess;
        _logger = logger;
    }

    /// <summary>
    /// Lists Review and Quarantined decisions on forms whose entries the user may see in Forms, newest first, with
    /// each entry's fields read live from Forms.
    /// </summary>
    [HttpGet("review")]
    [Authorize(Policy = FormsGuardPermissions.ReviewPolicy)]
    [ProducesResponseType<ReviewQueuePagedResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ReviewQueuePagedResult GetReviewQueue(int skip = 0, int take = 20)
    {
        (skip, take) = DecisionMapping.ClampPaging(skip, take);
        var formIds = _formAccess.EntryFormIds();
        if (formIds.Count == 0)
        {
            return new ReviewQueuePagedResult { Total = 0, Items = [], CanEditEntries = _formAccess.CanEditEntries() };
        }

        var (rows, total) = _repository.GetPage(skip, take, new DecisionQuery(QueueStatuses, null, null, null, formIds));

        // Per-request cache: each form is loaded once for the page, not once per row.
        var forms = new Dictionary<Guid, Form?>();
        return new ReviewQueuePagedResult
        {
            Total = total,
            Items = rows.Select(row => ToQueueItem(row, forms)).ToList(),
            CanEditEntries = _formAccess.CanEditEntries(),
        };
    }

    /// <summary>Approves a Review entry.</summary>
    [HttpPost("review/{recordId:guid}/approve")]
    [Authorize(Policy = FormsGuardPermissions.ReviewPolicy)]
    [ProducesResponseType<ReviewActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ReviewActionResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ReviewActionResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Approve(Guid recordId, CancellationToken cancellationToken) =>
        RunAsync(recordId, (key) => _reviewService.ApproveAsync(recordId, key, cancellationToken));

    /// <summary>Confirms a Review entry as spam; the Forms record is rejected.</summary>
    [HttpPost("review/{recordId:guid}/confirm-spam")]
    [Authorize(Policy = FormsGuardPermissions.ReviewPolicy)]
    [ProducesResponseType<ReviewActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ReviewActionResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ReviewActionResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ConfirmSpam(Guid recordId, CancellationToken cancellationToken) =>
        RunAsync(recordId, (key) => _reviewService.ConfirmSpamAsync(recordId, key, cancellationToken));

    /// <summary>Restores a Quarantined entry; the Forms record is approved.</summary>
    [HttpPost("review/{recordId:guid}/restore")]
    [Authorize(Policy = FormsGuardPermissions.ReviewPolicy)]
    [ProducesResponseType<ReviewActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ReviewActionResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ReviewActionResponse>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Restore(Guid recordId, CancellationToken cancellationToken) =>
        RunAsync(recordId, (key) => _reviewService.RestoreAsync(recordId, key, cancellationToken));

    /// <summary>Maps a review result to its HTTP response: 200 Done, 404 NotFound, 409 Refused, 500 Failed.</summary>
    public static IActionResult ToActionResult(ReviewResult result)
    {
        if (result.Outcome == ReviewOutcome.NotFound)
        {
            return new NotFoundResult();
        }

        var statusCode = result.Outcome switch
        {
            ReviewOutcome.Done => StatusCodes.Status200OK,
            ReviewOutcome.Refused => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };

        return new ObjectResult(new ReviewActionResponse
        {
            Outcome = result.Outcome.ToString(),
            Status = result.Status?.ToString(),
        })
        {
            StatusCode = statusCode,
        };
    }

    /// <summary>
    /// Runs a review action after Forms' own checks: 404 when the row is missing or its form's entries are not
    /// visible to the user (so the form's existence is not leaked), 403 without the edit-entries right.
    /// </summary>
    private async Task<IActionResult> RunAsync(Guid recordId, Func<Guid, Task<ReviewResult>> action)
    {
        // The reviewer is always the authenticated backoffice user, never a value from the request.
        if (_backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser?.Key is not { } reviewerKey)
        {
            return Unauthorized();
        }

        // EntryFormIds covers form access, the view-entries right and the form still existing.
        if (_repository.GetByRecordId(recordId) is not { } row || !_formAccess.EntryFormIds().Contains(row.FormId))
        {
            return NotFound();
        }

        if (!_formAccess.CanEditEntries())
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return ToActionResult(await action(reviewerKey));
    }

    private ReviewQueueItem ToQueueItem(DecisionDto row, Dictionary<Guid, Form?> forms)
    {
        var decision = DecisionMapping.ToListItem(row, _logger);
        try
        {
            if (!forms.TryGetValue(row.FormId, out var form))
            {
                form = _formService.Get(row.FormId);
                forms[row.FormId] = form;
            }

            var record = form is null ? null : _recordStorage.GetRecordByUniqueId(row.RecordId, form);
            if (form is null || record is null)
            {
                return new ReviewQueueItem { Decision = decision, FormName = form?.Name, RecordMissing = true };
            }

            return new ReviewQueueItem
            {
                Decision = decision,
                FormName = form.Name,
                Fields = DecisionStateBuilder.FromRecord(form, record)
                    .Where(f => !DecisionStateBuilder.IsExcludedType(f.FieldTypeId))
                    .Where(f => !string.IsNullOrWhiteSpace(f.Value))
                    .Select(f => new ReviewQueueField { Caption = f.Caption, Value = f.Value })
                    .ToList(),
            };
        }
        catch (Exception ex)
        {
            // Logs ids only; field values never reach the log.
            _logger.LogWarning(
                ex, "FormsGuard: could not read the Forms record {RecordId} (form {FormId}) for the review queue",
                row.RecordId, row.FormId);
            return new ReviewQueueItem { Decision = decision, RecordMissing = true };
        }
    }
}

using Asp.Versioning;
using Cogworks.Umbraco.FormsGuard.Api.Models;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Forms.Core.Services;

namespace Cogworks.Umbraco.FormsGuard.Api;

[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Decisions")]
public sealed class DecisionsController : FormsGuardApiControllerBase
{
    private readonly IDecisionRepository _repository;
    private readonly IUserNameResolver _userNameResolver;
    private readonly IFormService _formService;
    private readonly IFormAccess _formAccess;
    private readonly ILogger<DecisionsController> _logger;

    public DecisionsController(
        IDecisionRepository repository,
        IUserNameResolver userNameResolver,
        IFormService formService,
        IFormAccess formAccess,
        ILogger<DecisionsController> logger)
    {
        _repository = repository;
        _userNameResolver = userNameResolver;
        _formService = formService;
        _formAccess = formAccess;
        _logger = logger;
    }

    /// <summary>
    /// Lists decisions, newest first, filtered by form, status and creation time (<paramref name="fromUtc"/> inclusive,
    /// <paramref name="toUtc"/> exclusive). <c>ApprovedNotCheckedTotal</c> counts the form and dates only. Only
    /// forms whose entries the user may see in Forms are included.
    /// </summary>
    [HttpGet("decisions")]
    [Authorize(Policy = FormsGuardPermissions.ReviewPolicy)]
    [ProducesResponseType<DecisionPagedResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<DecisionPagedResult> GetDecisions(
        int skip = 0,
        int take = 20,
        Guid? formId = null,
        DecisionStatus? status = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null)
    {
        (skip, take) = DecisionMapping.ClampPaging(skip, take);
        var from = DecisionMapping.AsUtc(fromUtc);
        var to = DecisionMapping.AsUtc(toUtc);
        var statuses = status is { } s ? new[] { s.ToString() } : null;

        var formIds = _formAccess.EntryFormIds();
        if (!AnyVisible(formIds, formId))
        {
            return new DecisionPagedResult { Total = 0, ApprovedNotCheckedTotal = 0, Items = [] };
        }

        var (rows, total) = _repository.GetPage(skip, take, new DecisionQuery(statuses, formId, from, to, formIds));
        var notChecked = _repository.CountDecisions(
            new DecisionQuery([nameof(DecisionStatus.ApprovedNotChecked)], formId, from, to, formIds));
        var names = await _userNameResolver.ResolveAsync(rows.Select(r => r.Reviewer));

        return new DecisionPagedResult
        {
            Total = total,
            ApprovedNotCheckedTotal = notChecked,
            Items = rows.Select(r => DecisionMapping.ToListItem(r, _logger, NameOf(names, r.Reviewer))).ToList(),
        };
    }

    /// <summary>Lists the forms whose entries the user may see, by name, for filtering the log.</summary>
    [HttpGet("decisions/forms")]
    [Authorize(Policy = FormsGuardPermissions.ReviewPolicy)]
    [ProducesResponseType<IReadOnlyList<LogFormOption>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IReadOnlyList<LogFormOption> GetLogForms()
    {
        var formIds = _formAccess.EntryFormIds();
        if (formIds.Count == 0)
        {
            return [];
        }

        return _formService.GetSlim()
            .Where(f => formIds.Contains(f.Id))
            .Select(f => new LogFormOption { Id = f.Id, Name = f.Name ?? string.Empty })
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Id)
            .ToList();
    }

    /// <summary>
    /// Lists audit rows, newest first, filtered by form and creation time (<paramref name="fromUtc"/> inclusive,
    /// <paramref name="toUtc"/> exclusive). Only forms whose entries the user may see in Forms are included, so
    /// rows with no form are never shown.
    /// </summary>
    [HttpGet("audit")]
    [Authorize(Policy = FormsGuardPermissions.ReviewPolicy)]
    [ProducesResponseType<AuditPagedResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<AuditPagedResult> GetAudit(
        int skip = 0,
        int take = 20,
        Guid? formId = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null)
    {
        (skip, take) = DecisionMapping.ClampPaging(skip, take);
        var formIds = _formAccess.EntryFormIds();
        if (!AnyVisible(formIds, formId))
        {
            return new AuditPagedResult { Total = 0, Items = [] };
        }

        var query = new AuditQuery(formId, DecisionMapping.AsUtc(fromUtc), DecisionMapping.AsUtc(toUtc), formIds);
        var (rows, total) = _repository.GetAuditPage(skip, take, query);
        var names = await _userNameResolver.ResolveAsync(rows.Select(r => r.Actor));

        return new AuditPagedResult
        {
            Total = total,
            Items = rows.Select(r => new AuditListItem
            {
                Id = r.Id,
                RecordId = r.RecordId,
                FormId = r.FormId,
                Action = r.Action,
                Actor = r.Actor,
                ActorName = NameOf(names, r.Actor),
                Detail = r.Detail,
                CreatedUtc = DecisionMapping.AsUtc(r.CreatedUtc),
            }).ToList(),
        };
    }

    /// <summary>False when nothing can match: no visible forms, or an explicit form outside them.</summary>
    private static bool AnyVisible(IReadOnlySet<Guid> formIds, Guid? formId) =>
        formIds.Count > 0 && (formId is not { } id || formIds.Contains(id));

    private static string? NameOf(IReadOnlyDictionary<string, string> names, string? actor) =>
        actor is not null && names.TryGetValue(actor, out var name) ? name : actor;
}

using Cogworks.Umbraco.FormsGuard.Decisions;
using Microsoft.Extensions.Logging;
using Umbraco.Forms.Core.Models;
using Umbraco.Forms.Core.Persistence.Dtos;
using Umbraco.Forms.Core.Services;

namespace Cogworks.Umbraco.FormsGuard.Processing;

/// <summary>Applies a decision to the Forms record and runs the registered outcome handlers. The one apply path.</summary>
public interface IOutcomeApplier
{
    /// <summary>Approves the Forms record for Approved or ApprovedNotChecked, rejects it for Quarantined; Review leaves it Submitted.</summary>
    Task ApplyToRecordAsync(DecisionStatus status, Record record, Form form);

    /// <summary>Runs every registered outcome handler. A failing handler is logged and does not stop the others.</summary>
    Task RunHandlersAsync(DecisionOutcome outcome, CancellationToken cancellationToken);
}

public sealed class OutcomeApplier : IOutcomeApplier
{
    private readonly IRecordService _recordService;
    private readonly IEnumerable<IOutcomeHandler> _outcomeHandlers;
    private readonly ILogger<OutcomeApplier> _logger;

    public OutcomeApplier(
        IRecordService recordService,
        IEnumerable<IOutcomeHandler> outcomeHandlers,
        ILogger<OutcomeApplier> logger)
    {
        _recordService = recordService;
        _outcomeHandlers = outcomeHandlers;
        _logger = logger;
    }

    public async Task ApplyToRecordAsync(DecisionStatus status, Record record, Form form)
    {
        switch (status)
        {
            case DecisionStatus.Approved:
            case DecisionStatus.ApprovedNotChecked:
                await _recordService.ApproveAsync(record, form);
                break;
            case DecisionStatus.Quarantined:
                await _recordService.RejectAsync(record, form);
                break;
            default:
                break;
        }
    }

    public async Task RunHandlersAsync(DecisionOutcome outcome, CancellationToken cancellationToken)
    {
        foreach (var handler in _outcomeHandlers)
        {
            try
            {
                await handler.HandleAsync(outcome, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex, "FormsGuard: outcome handler {Handler} failed for record {RecordId} (form {FormId})",
                    handler.GetType().FullName, outcome.RecordId, outcome.FormId);
            }
        }
    }
}

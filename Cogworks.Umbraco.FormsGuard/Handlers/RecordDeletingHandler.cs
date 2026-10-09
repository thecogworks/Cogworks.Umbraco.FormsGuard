using Cogworks.Umbraco.FormsGuard.Persistence;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Forms.Core.Services.Notifications;

namespace Cogworks.Umbraco.FormsGuard.Handlers;

/// <summary>
/// Removes the decision rows of Forms records being deleted (single, bulk and scheduled deletes) and audits each one.
/// Runs whether or not the package is enabled. Never throws and never cancels the Forms delete.
/// </summary>
public sealed class RecordDeletingHandler : INotificationAsyncHandler<RecordDeletingNotification>
{
    /// <summary>The audit detail for a decision row removed with its Forms record.</summary>
    public const string Detail = "Forms record deleted";

    private readonly IDecisionRepository _repository;
    private readonly ILogger<RecordDeletingHandler> _logger;

    public RecordDeletingHandler(IDecisionRepository repository, ILogger<RecordDeletingHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public Task HandleAsync(RecordDeletingNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var ids = notification.DeletedEntities
                .Where(x => x is not null)
                .Select(x => x.UniqueId)
                .Distinct()
                .ToList();
            if (ids.Count == 0)
            {
                return Task.CompletedTask;
            }

            var deleted = _repository.DeleteForRecords(ids, Detail);
            if (deleted > 0)
            {
                _logger.LogInformation(
                    "FormsGuard: removed {Count} decision row(s) for {RecordCount} deleted Forms record(s)", deleted, ids.Count);
            }
        }
        catch (Exception ex)
        {
            // The orphan sweep removes anything left behind on a later job run.
            _logger.LogError(ex, "FormsGuard: failed to remove decision rows for deleted Forms records");
        }

        return Task.CompletedTask;
    }
}

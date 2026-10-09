using Cogworks.Umbraco.FormsGuard.Configuration;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Forms.Core.Data.Storage;
using Umbraco.Forms.Core.Enums;
using Umbraco.Forms.Core.Services;

namespace Cogworks.Umbraco.FormsGuard.Processing;

/// <summary>Deletes Forms records whose decision has been Quarantined longer than the retention period.</summary>
public interface IQuarantinePurge
{
    /// <summary>Runs one purge. Never throws.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Deletes, through Forms, each record whose decision has been Quarantined for more than
/// <see cref="FormsGuardOptions.QuarantineRetentionDays"/> days, with a <c>quarantine-purge</c> audit row each. The
/// record-deleting handler removes the decision row. Skips records no longer Rejected in Forms. Ignores <c>Enabled</c> and
/// <c>KillSwitch</c>; 0 days turns it off.
/// </summary>
public sealed class QuarantinePurge : IQuarantinePurge
{
    private const int BatchSize = 100;

    private readonly IDecisionRepository _repository;
    private readonly IFormService _formService;
    private readonly IRecordStorage _recordStorage;
    private readonly IRecordService _recordService;
    private readonly IOptionsMonitor<FormsGuardOptions> _options;
    private readonly ILogger<QuarantinePurge> _logger;

    public QuarantinePurge(
        IDecisionRepository repository,
        IFormService formService,
        IRecordStorage recordStorage,
        IRecordService recordService,
        IOptionsMonitor<FormsGuardOptions> options,
        ILogger<QuarantinePurge> logger)
    {
        _repository = repository;
        _formService = formService;
        _recordStorage = recordStorage;
        _recordService = recordService;
        _options = options;
        _logger = logger;
    }

    /// <summary>The time before which a quarantine has expired, or null when the purge is off (<paramref name="days"/> ≤ 0).</summary>
    public static DateTime? CutoffUtc(DateTime nowUtc, int days) => days <= 0 ? null : nowUtc.AddDays(-days);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var days = _options.CurrentValue.QuarantineRetentionDays;
            if (CutoffUtc(DateTime.UtcNow, days) is not { } cutoff)
            {
                return;
            }

            var detail = $"Quarantined for more than {days} days";
            var purged = 0;
            var afterId = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var rows = _repository.GetQuarantinedBefore(cutoff, afterId, BatchSize);
                if (rows.Count == 0)
                {
                    break;
                }

                foreach (var row in rows)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    afterId = row.Id;
                    try
                    {
                        var form = _formService.Get(row.FormId);
                        var record = form is null ? null : _recordStorage.GetRecordByUniqueId(row.RecordId, form);
                        if (form is null || record is null)
                        {
                            _logger.LogDebug(
                                "FormsGuard: quarantine purge skipped record {RecordId}; record or form {FormId} no longer exists",
                                row.RecordId, row.FormId);
                            continue;
                        }

                        // Re-read: a reviewer may have restored the entry since the batch was read.
                        var current = _repository.GetByRecordId(row.RecordId);
                        if (current is null
                            || current.Status != nameof(DecisionStatus.Quarantined)
                            || current.UpdatedUtc >= cutoff)
                        {
                            _logger.LogDebug(
                                "FormsGuard: quarantine purge skipped record {RecordId}; its decision changed since the batch was read",
                                row.RecordId);
                            continue;
                        }

                        // An editor may have approved the entry in Forms' own Entries screen; never delete it then.
                        if (record.State != FormState.Rejected)
                        {
                            _logger.LogInformation(
                                "FormsGuard: quarantine purge skipped record {RecordId}; its Forms state is {State}, not Rejected",
                                row.RecordId, record.State);
                            continue;
                        }

                        await _recordService.DeleteAsync(record, form);
                        purged++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "FormsGuard: quarantine purge could not delete record {RecordId} (form {FormId})", row.RecordId, row.FormId);
                        continue;
                    }

                    try
                    {
                        _repository.InsertSystemAudit(row, DecisionRepository.QuarantinePurgeAction, detail);
                    }
                    catch (Exception ex)
                    {
                        // The record is gone; the deleting handler's record-deleted row still records it.
                        _logger.LogError(ex, "FormsGuard: quarantine purge deleted record {RecordId} but could not write its audit row", row.RecordId);
                    }
                }

                if (rows.Count < BatchSize)
                {
                    break;
                }
            }

            if (purged > 0)
            {
                _logger.LogInformation(
                    "FormsGuard: quarantine purge deleted {Count} Forms record(s) quarantined for more than {Days} days", purged, days);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FormsGuard: quarantine purge failed");
        }
    }
}

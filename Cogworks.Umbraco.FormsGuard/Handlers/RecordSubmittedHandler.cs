using Cogworks.Umbraco.FormsGuard.Configuration;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Forms.Core.Enums;
using Umbraco.Forms.Core.Services.Notifications;

namespace Cogworks.Umbraco.FormsGuard.Handlers;

/// <summary>
/// Queues a pending decision for each entry saved on a guarded form. Never blocks or alters the submit.
/// </summary>
public sealed class RecordSubmittedHandler : INotificationAsyncHandler<RecordSubmittedNotification>
{
    private readonly IDecisionRepository _repository;
    private readonly IFormSettingsReader _settingsReader;
    private readonly IOptionsMonitor<FormsGuardOptions> _options;
    private readonly ILogger<RecordSubmittedHandler> _logger;

    public RecordSubmittedHandler(
        IDecisionRepository repository,
        IFormSettingsReader settingsReader,
        IOptionsMonitor<FormsGuardOptions> options,
        ILogger<RecordSubmittedHandler> logger)
    {
        _repository = repository;
        _settingsReader = settingsReader;
        _options = options;
        _logger = logger;
    }

    public Task HandleAsync(RecordSubmittedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            if (!_options.CurrentValue.Enabled)
            {
                return Task.CompletedTask;
            }

            var record = notification.Record;
            var formId = notification.Form.Id;

            if (!_settingsReader.Get(formId).Guarded)
            {
                return Task.CompletedTask;
            }

            if (record.State != FormState.Submitted)
            {
                _logger.LogWarning(
                    "FormsGuard: record {RecordId} on guarded form {FormId} is {State}, not Submitted; turn on manual approval for this form. Skipping.",
                    record.UniqueId, formId, record.State);
                return Task.CompletedTask;
            }

            if (_repository.InsertPending(record.UniqueId, formId))
            {
                _logger.LogInformation(
                    "FormsGuard: queued record {RecordId} (form {FormId}) as Pending", record.UniqueId, formId);
            }
            else
            {
                _logger.LogInformation(
                    "FormsGuard: record {RecordId} (form {FormId}) already has a decision row", record.UniqueId, formId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FormsGuard: failed to queue decision for record {RecordId}", notification.Record?.UniqueId);
        }

        return Task.CompletedTask;
    }
}

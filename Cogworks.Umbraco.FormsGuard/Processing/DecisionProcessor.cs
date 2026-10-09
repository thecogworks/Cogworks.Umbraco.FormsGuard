using System.Text.Json;
using Cogworks.Umbraco.FormsGuard.Configuration;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Cogworks.Umbraco.FormsGuard.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Forms.Core.Data.Storage;
using Umbraco.Forms.Core.Enums;
using Umbraco.Forms.Core.Models;
using Umbraco.Forms.Core.Persistence.Dtos;
using Umbraco.Forms.Core.Services;

namespace Cogworks.Umbraco.FormsGuard.Processing;

/// <summary>Decides one claimed row: asks the provider, and approves, quarantines or leaves the Forms record for review.</summary>
public interface IDecisionProcessor
{
    /// <summary>
    /// Processes one claimed row. Never throws; failures release the row on the retry schedule until the attempt cap,
    /// then the form's failure policy applies.
    /// </summary>
    Task ProcessAsync(DecisionDto row, CancellationToken cancellationToken);
}

public sealed class DecisionProcessor : IDecisionProcessor
{
    private readonly IDecisionRepository _repository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IDecisionProvider _provider;
    private readonly IEnumerable<IQuestionContributor> _contributors;
    private readonly IFormService _formService;
    private readonly IRecordStorage _recordStorage;
    private readonly IOutcomeApplier _applier;
    private readonly IFormSettingsReader _settingsReader;
    private readonly IOptionsMonitor<FormsGuardOptions> _options;
    private readonly ILogger<DecisionProcessor> _logger;

    public DecisionProcessor(
        IDecisionRepository repository,
        ISettingsRepository settingsRepository,
        IEnumerable<IDecisionProvider> providers,
        IEnumerable<IQuestionContributor> contributors,
        IFormService formService,
        IRecordStorage recordStorage,
        IOutcomeApplier applier,
        IFormSettingsReader settingsReader,
        IOptionsMonitor<FormsGuardOptions> options,
        ILogger<DecisionProcessor> logger)
    {
        _repository = repository;
        _settingsRepository = settingsRepository;
        _provider = DecisionProviders.Select(providers, options.CurrentValue.Provider);
        _contributors = contributors;
        _formService = formService;
        _recordStorage = recordStorage;
        _applier = applier;
        _settingsReader = settingsReader;
        _options = options;
        _logger = logger;
    }

    public async Task ProcessAsync(DecisionDto row, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessRowAsync(row, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FormsGuard: error deciding record {RecordId} (form {FormId})", row.RecordId, row.FormId);
            if (TryRelease(row, null))
            {
                await _applier.RunHandlersAsync(NoAnswers(row, DecisionStatus.Review), cancellationToken);
            }
        }
    }

    private async Task ProcessRowAsync(DecisionDto row, CancellationToken cancellationToken)
    {
        var form = _formService.Get(row.FormId);
        var record = form is null ? null : _recordStorage.GetRecordByUniqueId(row.RecordId, form);
        if (form is null || record is null)
        {
            _logger.LogWarning(
                "FormsGuard: record {RecordId} or form {FormId} no longer exists; deleting decision row",
                row.RecordId, row.FormId);
            DeleteRow(row, ProcessorStep.DeleteMissing, null);
            return;
        }

        // Facts are computed once the record exists so the branch choice stays a plain function of values.
        var formSettings = _settingsReader.Get(form.Id);
        var settings = formSettings.Settings;
        var fields = DecisionStateBuilder.FromRecord(form, record);
        var hit = HardRules.Evaluate(LoadRules(row), fields, settings.EmailFieldId);
        var questions = MergeQuestions(row, form.Id, settings);
        var options = _options.CurrentValue;

        switch (ProcessorBranch.Choose(record.State, formSettings.Guarded, hit is not null, questions.Count, options.KillSwitch))
        {
            case ProcessorStep.DeleteMissing:
                // Unreachable: the record exists here; kept so every step is handled.
                DeleteRow(row, ProcessorStep.DeleteMissing, record.State);
                return;

            case ProcessorStep.CompleteFromRecord:
            {
                var recordStatus = ProcessorBranch.FromRecordState(record.State)!.Value;
                _repository.CompleteFromRecord(row, recordStatus);
                _logger.LogInformation(
                    "FormsGuard: record {RecordId} (form {FormId}) is already {State}; decision completed {Status} from the record",
                    row.RecordId, row.FormId, record.State, recordStatus);
                await _applier.RunHandlersAsync(NoAnswers(row, recordStatus), cancellationToken);
                return;
            }

            case ProcessorStep.DeleteOtherState:
                _logger.LogInformation(
                    "FormsGuard: record {RecordId} (form {FormId}) is already {State}; deleting decision row",
                    row.RecordId, row.FormId, record.State);
                DeleteRow(row, ProcessorStep.DeleteOtherState, record.State);
                return;

            case ProcessorStep.SkipUnguarded:
                _logger.LogInformation(
                    "FormsGuard: form {FormId} is no longer guarded; deleting decision row for record {RecordId}",
                    row.FormId, row.RecordId);
                DeleteRow(row, ProcessorStep.SkipUnguarded, record.State);
                return;

            case ProcessorStep.Rule:
                await _applier.ApplyToRecordAsync(hit!.Status, record, form);

                _repository.CompleteByRule(row, hit.Status, hit.ToString());
                _logger.LogInformation(
                    "FormsGuard: record {RecordId} (form {FormId}) decided {Status} by rule {RuleType} {RuleId}; no provider call",
                    row.RecordId, row.FormId, hit.Status, hit.Type, hit.RuleId);
                await _applier.RunHandlersAsync(NoAnswers(row, hit.Status), cancellationToken);
                return;

            case ProcessorStep.NoQuestions:
                _repository.CompleteWithoutCall(row, DecisionStatus.Review, "settings", "no questions to ask");
                _logger.LogInformation(
                    "FormsGuard: record {RecordId} (form {FormId}) has no questions to ask; left for Review",
                    row.RecordId, row.FormId);
                await _applier.RunHandlersAsync(NoAnswers(row, DecisionStatus.Review), cancellationToken);
                return;

            case ProcessorStep.KillSwitch:
                await ApplyPolicyAsync(row, record, form, settings.FailurePolicy, "kill switch, no provider call", providerCalled: false, cancellationToken);
                return;

            case ProcessorStep.CallProvider:
                break;
        }

        var request = new DecisionRequest(
            form.Id,
            DecisionStateBuilder.Build(settings, form.Name, fields),
            questions);

        var result = await _provider.DecideAsync(request, cancellationToken);
        if (!result.Success)
        {
            if (result.Retryable)
            {
                _logger.LogWarning(
                    "FormsGuard: provider {Provider} failed for record {RecordId} (form {FormId}); retryable: {Error}",
                    _provider.Alias, row.RecordId, row.FormId, result.Error);
            }
            else
            {
                _logger.LogError(
                    "FormsGuard: provider {Provider} failed for record {RecordId} (form {FormId}) and will not retry: {Error}",
                    _provider.Alias, row.RecordId, row.FormId, result.Error);
            }

            // Read options afresh so a kill switch turned on during the call applies, as before.
            var current = _options.CurrentValue;
            var step = ProcessorBranch.OnProviderFailure(result.Retryable, current.KillSwitch, row.Attempts + 1, current.MaxAttempts);
            if (step == ProviderFailureStep.ApplyPolicy)
            {
                var reason = !result.Retryable ? "non-retryable provider failure"
                    : current.KillSwitch ? "kill switch" : "attempt cap";
                await ApplyPolicyAsync(row, record, form, settings.FailurePolicy, reason, providerCalled: true, cancellationToken);
            }
            else
            {
                TryRelease(row, result.RetryAfter);
            }

            return;
        }

        // The rule only reads guard.* questions; contributed ones are informational whatever role they declare.
        var status = DecisionRule.Evaluate(questions, result.Answers, settings.Thresholds);

        await _applier.ApplyToRecordAsync(status, record, form);

        var probabilities = JsonSerializer.Serialize(
            result.Answers
                .GroupBy(a => a.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Probability));
        _repository.Complete(row, status, _provider.Alias, result.ModelVersion, probabilities);

        _logger.LogInformation(
            "FormsGuard: record {RecordId} (form {FormId}) decided {Status} by provider {Provider}",
            row.RecordId, row.FormId, status, _provider.Alias);

        await _applier.RunHandlersAsync(new DecisionOutcome(row.RecordId, row.FormId, status, result.Answers), cancellationToken);
    }

    /// <summary>The form's enabled core questions merged with contributed ones; rejected contributions are logged.</summary>
    private IReadOnlyList<DecisionQuestion> MergeQuestions(DecisionDto row, Guid formId, FormGuardSettings settings)
    {
        var coreQuestions = settings.Questions
            .Where(q => q.Enabled)
            .Select(q => new DecisionQuestion(
                q.Key, q.Text, q.Role, QuestionType.Noul, TrueCriteria: q.TrueCriteria, FalseCriteria: q.FalseCriteria))
            .ToList();

        var merged = QuestionMerger.Merge(
            coreQuestions,
            _contributors.Select(c => (
                Source: c.GetType().FullName ?? c.GetType().Name,
                Get: (Func<IEnumerable<DecisionQuestion>>)(() => c.GetQuestions(formId)))));

        foreach (var rejection in merged.Rejections)
        {
            if (rejection.Key is null)
            {
                _logger.LogError(
                    "FormsGuard: question contributor {Contributor} threw {ExceptionType} for form {FormId}; its questions were skipped",
                    rejection.Source, rejection.Reason, row.FormId);
            }
            else
            {
                _logger.LogWarning(
                    "FormsGuard: question {QuestionKey} from contributor {Contributor} dropped for form {FormId}: {Reason}",
                    rejection.Key, rejection.Source, row.FormId, rejection.Reason);
            }
        }

        return merged.Questions;
    }

    private async Task ApplyPolicyAsync(
        DecisionDto row, Record record, Form form, FailurePolicy policy, string reason, bool providerCalled,
        CancellationToken cancellationToken)
    {
        var status = FailurePolicyRule.Outcome(policy);
        await _applier.ApplyToRecordAsync(status, record, form);

        if (providerCalled)
        {
            _repository.CompleteByPolicy(row, status, reason);
        }
        else
        {
            _repository.CompleteWithoutCall(row, status, "policy", reason);
        }

        _logger.LogInformation(
            "FormsGuard: record {RecordId} (form {FormId}) decided {Status} by failure policy {Policy} ({Reason}) after {Attempts} attempts",
            row.RecordId, row.FormId, status, policy, reason, row.Attempts);
        await _applier.RunHandlersAsync(NoAnswers(row, status), cancellationToken);
    }

    private void DeleteRow(DecisionDto row, ProcessorStep step, FormState? state)
    {
        var (action, detail) = ProcessorBranch.RemovalAudit(step, state);
        _repository.DeleteWithAudit(row, action, detail);
    }

    private static DecisionOutcome NoAnswers(DecisionDto row, DecisionStatus status) =>
        new(row.RecordId, row.FormId, status, Array.Empty<DecisionAnswer>());

    private List<HardRule> LoadRules(DecisionDto row)
    {
        var rules = new List<HardRule>();
        foreach (var dto in _settingsRepository.GetRules(row.FormId))
        {
            if (string.IsNullOrWhiteSpace(dto.Pattern))
            {
                continue;
            }

            if (!HardRules.TryParse(dto.RuleType, out var type))
            {
                _logger.LogWarning(
                    "FormsGuard: rule {RuleId} (form {FormId}) has an unknown rule type; skipped",
                    dto.Id, row.FormId);
                continue;
            }

            rules.Add(new HardRule(dto.Id, type, dto.Pattern));
        }

        return rules;
    }

    /// <summary>Releases the row for retry, or at the attempt cap leaves it for Review. True when the row was finished by policy.</summary>
    private bool TryRelease(DecisionDto row, TimeSpan? retryAfter)
    {
        try
        {
            var options = _options.CurrentValue;
            var failures = row.Attempts + 1;
            if (FailurePolicyRule.CapReached(failures, options.MaxAttempts))
            {
                // The Forms record call may be what keeps failing, so leave the record as it is and send it to review.
                _repository.CompleteByPolicy(row, DecisionStatus.Review, "attempt cap");
                _logger.LogWarning(
                    "FormsGuard: record {RecordId} (form {FormId}) left for Review by failure policy after {Attempts} failed attempts",
                    row.RecordId, row.FormId, row.Attempts);
                return true;
            }

            var delay = RetryBackoff.NextDelay(failures, retryAfter, options.RetryBaseSeconds, options.RetryMaxSeconds);
            _repository.Release(row, DateTime.UtcNow.Add(delay));
            _logger.LogInformation(
                "FormsGuard: record {RecordId} released after attempt {Attempts}; next attempt in {DelaySeconds}s",
                row.RecordId, row.Attempts, (int)delay.TotalSeconds);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FormsGuard: could not release decision row for record {RecordId}", row.RecordId);
            return false;
        }
    }
}

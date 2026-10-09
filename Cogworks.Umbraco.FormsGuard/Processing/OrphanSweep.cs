using Cogworks.Umbraco.FormsGuard.Configuration;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cogworks.Umbraco.FormsGuard.Processing;

/// <summary>Removes non-<c>Pending</c> decision rows whose Forms record no longer exists.</summary>
public interface IOrphanSweep
{
    /// <summary>Runs one sweep. Never throws.</summary>
    void Run();
}

/// <summary>
/// Removes decision rows whose Forms record was deleted without a notification (for example directly in SQL), with a
/// <c>record-deleted</c> audit row each. Pending rows are left to the processor's missing-record path.
/// </summary>
public sealed class OrphanSweep : IOrphanSweep
{
    /// <summary>The audit detail for a decision row removed by the sweep.</summary>
    public const string Detail = "Forms record missing";

    private readonly IDecisionRepository _repository;
    private readonly IOptionsMonitor<FormsGuardOptions> _options;
    private readonly ILogger<OrphanSweep> _logger;

    public OrphanSweep(IDecisionRepository repository, IOptionsMonitor<FormsGuardOptions> options, ILogger<OrphanSweep> logger)
    {
        _repository = repository;
        _options = options;
        _logger = logger;
    }

    public void Run()
    {
        try
        {
            var take = (int)Math.Clamp(_options.CurrentValue.ProcessorBatchSize * 10L, 1, int.MaxValue);
            var ids = _repository.GetOrphanRecordIds(take);
            if (ids.Count == 0)
            {
                return;
            }

            var deleted = _repository.DeleteForRecords(ids, Detail);
            _logger.LogWarning(
                "FormsGuard: removed {Count} decision row(s) whose Forms record is missing", deleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FormsGuard: orphan sweep failed");
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Cogworks.Umbraco.FormsGuard.Processing;

/// <summary>
/// Daily job that purges expired quarantined records. Runs on Single and SchedulingPublisher servers only (base default).
/// </summary>
public sealed class QuarantinePurgeJob : RecurringBackgroundJobBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QuarantinePurgeJob> _logger;

    public QuarantinePurgeJob(IServiceScopeFactory scopeFactory, ILogger<QuarantinePurgeJob> logger)
        : base(TimeSpan.FromDays(1))
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public override TimeSpan Delay => TimeSpan.FromMinutes(5);

    public override async Task RunJobAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IQuarantinePurge>().RunAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FormsGuard: quarantine purge job failed");
        }
    }
}

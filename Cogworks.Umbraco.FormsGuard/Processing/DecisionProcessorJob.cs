using Cogworks.Umbraco.FormsGuard.Configuration;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Hosting;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Cogworks.Umbraco.FormsGuard.Processing;

/// <summary>
/// Recurring job that decides pending entries. Runs on Single and SchedulingPublisher servers only (base default).
/// </summary>
public sealed class DecisionProcessorJob : RecurringBackgroundJobBase
{
    private static readonly string Claimant = $"{Environment.MachineName}:{Environment.ProcessId}";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDecisionRepository _repository;
    private readonly IOrphanSweep _orphanSweep;
    private readonly IOptionsMonitor<FormsGuardOptions> _options;
    private readonly ILogger<DecisionProcessorJob> _logger;

    public DecisionProcessorJob(
        IServiceScopeFactory scopeFactory,
        IDecisionRepository repository,
        IOrphanSweep orphanSweep,
        IOptionsMonitor<FormsGuardOptions> options,
        ILogger<DecisionProcessorJob> logger)
        : base(TimeSpan.FromSeconds(Math.Max(1, options.CurrentValue.ProcessorIntervalSeconds)))
    {
        _scopeFactory = scopeFactory;
        _repository = repository;
        _orphanSweep = orphanSweep;
        _options = options;
        _logger = logger;
    }

    public override TimeSpan Delay => TimeSpan.FromSeconds(15);

    public override async Task RunJobAsync(CancellationToken cancellationToken)
    {
        // Retention runs before the Enabled check: it only removes the package's own rows for deleted records.
        if (!cancellationToken.IsCancellationRequested)
        {
            _orphanSweep.Run();
        }

        var options = _options.CurrentValue;
        if (!options.Enabled || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var staleBefore = DateTime.UtcNow.AddSeconds(-options.ClaimStaleSeconds);
        var rows = _repository.ClaimPending(Math.Max(1, options.ProcessorBatchSize), Claimant, staleBefore);
        if (rows.Count == 0)
        {
            return;
        }

        // The job's token is not passed to ParallelOptions: every claimed row must reach a body so that,
        // on shutdown, rows not yet started are unclaimed rather than left for the stale period.
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(options.ProcessorMaxConcurrency, 1, 32) };
        await Parallel.ForEachAsync(rows, parallelOptions, async (row, _) =>
        {
            // ClaimPending left Umbraco's AsyncLocal ambient scope stack on this flow; suppress flow so each row
            // starts with its own clean stack instead of sharing one across parallel bodies.
            Task task;
            using (ExecutionContext.SuppressFlow())
            {
                task = Task.Run(async () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        TryUnclaim(row);
                        return;
                    }

                    await ProcessInScopeAsync(row, cancellationToken);
                });
            }

            await task;
        });
    }

    private async Task ProcessInScopeAsync(DecisionDto row, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();

            // Forms workflows (e.g. Razor email) need an HttpContext and an UmbracoContext; a background job has
            // neither, so provide a detached HttpContext on this scope, as Forms does for its own workflow runs.
            var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var previousHttpContext = httpContextAccessor.HttpContext;
            httpContextAccessor.HttpContext ??= CreateDetachedHttpContext(scope.ServiceProvider);
            try
            {
                using var umbracoContext = scope.ServiceProvider.GetRequiredService<IUmbracoContextFactory>().EnsureUmbracoContext();
                var processor = scope.ServiceProvider.GetRequiredService<IDecisionProcessor>();
                await processor.ProcessAsync(row, cancellationToken);
            }
            finally
            {
                httpContextAccessor.HttpContext = previousHttpContext;
            }
        }
        catch (Exception ex)
        {
            // The processor handles its own failures; this is scope or context set-up. The claim is left to go
            // stale so a persistent set-up fault does not retry every run.
            _logger.LogError(ex, "FormsGuard: could not set up processing for record {RecordId}; claim left to expire", row.RecordId);
        }
    }

    private void TryUnclaim(DecisionDto row)
    {
        try
        {
            _repository.Unclaim(row);
            _logger.LogInformation("FormsGuard: shutting down; record {RecordId} unclaimed", row.RecordId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FormsGuard: could not unclaim decision row for record {RecordId}", row.RecordId);
        }
    }

    // The UmbracoContext builds its request URL from scheme and host, so give it the site's main URL
    // (Umbraco:CMS:WebRouting:UmbracoApplicationUrl or detected), falling back to localhost.
    private static HttpContext CreateDetachedHttpContext(IServiceProvider services)
    {
        var mainUrl = services.GetRequiredService<IHostingEnvironment>().ApplicationMainUrl;
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Scheme = mainUrl?.Scheme ?? Uri.UriSchemeHttps;
        httpContext.Request.Host = mainUrl is null ? new HostString("localhost") : new HostString(mainUrl.Authority);
        return httpContext;
    }
}

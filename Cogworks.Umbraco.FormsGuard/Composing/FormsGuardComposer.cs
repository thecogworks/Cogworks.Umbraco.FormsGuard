using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Extensions;
using Cogworks.Umbraco.FormsGuard.Configuration;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Decisions.Jev;
using Cogworks.Umbraco.FormsGuard.Handlers;
using Cogworks.Umbraco.FormsGuard.Persistence;
using Cogworks.Umbraco.FormsGuard.Processing;
using Cogworks.Umbraco.FormsGuard.Review;
using Cogworks.Umbraco.FormsGuard.Settings;
using Umbraco.Forms.Core.Services.Notifications;

namespace Cogworks.Umbraco.FormsGuard.Composing;

public sealed class FormsGuardComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<FormsGuardOptions>()
            .Bind(builder.Config.GetSection(FormsGuardOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => o.Jev is not null && o.Jev.TimeoutSeconds is >= 1 and <= 120,
                "Cogworks:FormsGuard:Jev:TimeoutSeconds must be between 1 and 120.")
            .Validate(
                o => o.ProcessorMaxConcurrency is >= 1 and <= 32,
                "Cogworks:FormsGuard:ProcessorMaxConcurrency must be between 1 and 32.")
            .Validate(
                o => o.ClaimStaleSeconds >= 60,
                "Cogworks:FormsGuard:ClaimStaleSeconds must be at least 60.")
            .Validate(
                o => o.RetryBaseSeconds >= 1,
                "Cogworks:FormsGuard:RetryBaseSeconds must be at least 1.")
            .Validate(
                o => o.RetryMaxSeconds >= o.RetryBaseSeconds,
                "Cogworks:FormsGuard:RetryMaxSeconds must be at least RetryBaseSeconds.")
            .Validate(
                o => o.MaxAttempts >= 1,
                "Cogworks:FormsGuard:MaxAttempts must be at least 1.")
            .Validate(
                o => o.QuarantineRetentionDays >= 0,
                "Cogworks:FormsGuard:QuarantineRetentionDays must be 0 (off) or more.");

        builder.Services.AddSingleton<IDecisionRepository, DecisionRepository>();
        builder.Services.AddSingleton<ISettingsRepository, SettingsRepository>();
        builder.Services.AddSingleton<IFormSettingsReader, FormSettingsReader>();
        builder.Services.AddSingleton<StubDecisionProvider>();
        builder.Services.AddSingleton<IDecisionProvider>(sp => sp.GetRequiredService<StubDecisionProvider>());
        // Infinite client timeout so the provider's per-call TimeoutSeconds governs.
        builder.Services.AddHttpClient<JevDecisionProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        // Scoped, not singleton, so the typed HttpClient is not captured for the app's lifetime.
        builder.Services.AddScoped<IDecisionProvider>(sp => sp.GetRequiredService<JevDecisionProvider>());
        // Other packages (e.g. Cogworks.Umbraco.FormsGuard.UmbracoAI) add IDecisionProvider entries;
        // DecisionProcessor picks the one whose alias matches Cogworks:FormsGuard:Provider.
        builder.Services.AddScoped<IOutcomeApplier, OutcomeApplier>();
        builder.Services.AddScoped<IDecisionProcessor, DecisionProcessor>();
        builder.Services.AddScoped<IReviewService, ReviewService>();
        builder.Services.AddSingleton<IOrphanSweep, OrphanSweep>();
        builder.Services.AddRecurringBackgroundJob<DecisionProcessorJob>();
        builder.Services.AddScoped<IQuarantinePurge, QuarantinePurge>();
        builder.Services.AddRecurringBackgroundJob<QuarantinePurgeJob>();

        builder.AddNotificationAsyncHandler<RecordSubmittedNotification, RecordSubmittedHandler>();
        builder.AddNotificationAsyncHandler<RecordDeletingNotification, RecordDeletingHandler>();
    }
}

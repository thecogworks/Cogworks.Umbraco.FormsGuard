using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Cogworks.Umbraco.FormsGuard.Composing;
using Cogworks.Umbraco.FormsGuard.Decisions;

namespace Cogworks.Umbraco.FormsGuard.UmbracoAI;

/// <summary>Registers the <c>umbracoai</c> decision provider. Select it with <c>Cogworks:FormsGuard:Provider</c>.</summary>
[ComposeAfter(typeof(FormsGuardComposer))]
public sealed class UmbracoAIComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<UmbracoAIDecisionOptions>()
            .Bind(builder.Config.GetSection(UmbracoAIDecisionOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ProfileAlias),
                "Cogworks:FormsGuard:UmbracoAI:ProfileAlias is required.")
            .Validate(
                o => o.TimeoutSeconds is >= 1 and <= 120,
                "Cogworks:FormsGuard:UmbracoAI:TimeoutSeconds must be between 1 and 120.");

        builder.Services.AddScoped<IDecisionProvider, UmbracoAIDecisionProvider>();
    }
}

using System.ComponentModel.DataAnnotations;

namespace Cogworks.Umbraco.FormsGuard.UmbracoAI;

/// <summary>Settings for the Umbraco.AI decision provider, bound from <c>Cogworks:FormsGuard:UmbracoAI</c>.</summary>
public sealed class UmbracoAIDecisionOptions
{
    public const string SectionName = "Cogworks:FormsGuard:UmbracoAI";

    /// <summary>Alias of the Umbraco.AI profile (connection, model and settings) used for every call.</summary>
    [Required]
    public string ProfileAlias { get; set; } = "forms-guard";

    /// <summary>Seconds to wait for a reply before the call fails as retryable. 1 to 120.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 30;
}

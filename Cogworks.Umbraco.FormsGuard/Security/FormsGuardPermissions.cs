namespace Cogworks.Umbraco.FormsGuard.Security;

/// <summary>The one source of Forms Guard section, permission and policy names.</summary>
public static class FormsGuardPermissions
{
    /// <summary>Backoffice section alias; must match the client <c>section</c> manifest.</summary>
    public const string SectionAlias = "Cogworks.FormsGuard.Section";

    /// <summary>Entity type the client permission manifests are declared for.</summary>
    public const string EntityType = "cogworks-forms-guard";

    /// <summary>Permission verb: change Forms Guard settings.</summary>
    public const string ManageSettingsVerb = "Cogworks.FormsGuard.ManageSettings";

    /// <summary>Permission verb: list and review Forms Guard decisions.</summary>
    public const string ReviewVerb = "Cogworks.FormsGuard.Review";

    /// <summary>Policy: section access plus <see cref="ManageSettingsVerb"/>.</summary>
    public const string ManageSettingsPolicy = "Cogworks.FormsGuard.Policy.ManageSettings";

    /// <summary>Policy: section access plus <see cref="ReviewVerb"/>.</summary>
    public const string ReviewPolicy = "Cogworks.FormsGuard.Policy.Review";
}

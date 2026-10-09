using Microsoft.AspNetCore.Authorization;
using Umbraco.Cms.Api.Management.Security.Authorization;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security.Authorization;

namespace Cogworks.Umbraco.FormsGuard.Security;

/// <summary>Requires the Forms Guard section and the given permission verb on one of the user's groups.</summary>
public sealed class FormsGuardRequirement : IAuthorizationRequirement
{
    public FormsGuardRequirement(string verb) => Verb = verb;

    public string Verb { get; }
}

/// <summary>Checks <see cref="FormsGuardRequirement"/> against the current backoffice user.</summary>
public sealed class FormsGuardAuthorizationHandler : MustSatisfyRequirementAuthorizationHandler<FormsGuardRequirement>
{
    private readonly IAuthorizationHelper _authorizationHelper;

    public FormsGuardAuthorizationHandler(IAuthorizationHelper authorizationHelper) =>
        _authorizationHelper = authorizationHelper;

    protected override Task<bool> IsAuthorized(AuthorizationHandlerContext context, FormsGuardRequirement requirement)
    {
        if (!_authorizationHelper.TryGetUmbracoUser(context.User, out IUser? user) || user is null)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(IsAllowed(
            user.AllowedSections,
            user.Groups.Select(g => (IEnumerable<string>)g.Permissions),
            requirement.Verb));
    }

    /// <summary>True when the section is allowed and any group grants the verb.</summary>
    public static bool IsAllowed(
        IEnumerable<string> allowedSections,
        IEnumerable<IEnumerable<string>> groupPermissions,
        string verb) =>
        allowedSections.Contains(FormsGuardPermissions.SectionAlias, StringComparer.Ordinal)
        && groupPermissions.Any(p => p.Contains(verb, StringComparer.Ordinal));
}

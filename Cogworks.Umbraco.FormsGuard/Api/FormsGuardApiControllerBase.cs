using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Common.Filters;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;
using UmbConstants = Umbraco.Cms.Core.Constants;

namespace Cogworks.Umbraco.FormsGuard.Api;

/// <summary>
/// Base for Forms Guard Management API controllers. Requires backoffice access; each action adds its own
/// Forms Guard policy (section plus verb).
/// </summary>
[ApiController]
[BackOfficeRoute("formsguard/api/v{version:apiVersion}")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[MapToApi(FormsGuardApi.Name)]
[JsonOptionsName(UmbConstants.JsonOptionsNames.BackOffice)]
public abstract class FormsGuardApiControllerBase : ControllerBase
{
}

public static class FormsGuardApi
{
    /// <summary>OpenAPI document name; served at <c>/umbraco/swagger/formsguard/swagger.json</c>.</summary>
    public const string Name = "formsguard";
}

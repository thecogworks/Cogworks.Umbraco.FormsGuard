using Asp.Versioning;
using Cogworks.Umbraco.FormsGuard.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using OpenIddict.Validation.AspNetCore;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Api.Management.OpenApi;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Cogworks.Umbraco.FormsGuard.Api;

/// <summary>Registers the Forms Guard OpenAPI document and authorization policies.</summary>
public sealed class FormsGuardApiComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<IOperationIdHandler, FormsGuardOperationIdHandler>();
        builder.Services.AddScoped<IUserNameResolver, UserNameResolver>();
        builder.Services.AddScoped<IFormAccess, FormAccess>();

        builder.Services.Configure<SwaggerGenOptions>(o =>
        {
            o.SwaggerDoc(FormsGuardApi.Name, new OpenApiInfo { Title = "Forms Guard Management API", Version = "1.0" });
            o.OperationFilter<FormsGuardOperationSecurityFilter>();
        });

        // Singleton: Umbraco resolves IAuthorizationService from singletons, so a scoped handler breaks startup.
        builder.Services.AddSingleton<IAuthorizationHandler, FormsGuardAuthorizationHandler>();
        builder.Services.AddAuthorization(AddPolicies);
    }

    /// <summary>Adds each Forms Guard policy with its own permission verb.</summary>
    public static void AddPolicies(AuthorizationOptions options)
    {
        AddPolicy(options, FormsGuardPermissions.ManageSettingsPolicy, FormsGuardPermissions.ManageSettingsVerb);
        AddPolicy(options, FormsGuardPermissions.ReviewPolicy, FormsGuardPermissions.ReviewVerb);
    }

    private static void AddPolicy(AuthorizationOptions options, string name, string verb) =>
        options.AddPolicy(name, policy =>
        {
            policy.AuthenticationSchemes.Add(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
            policy.Requirements.Add(new FormsGuardRequirement(verb));
        });
}

/// <summary>Marks Forms Guard operations as requiring the backoffice bearer token.</summary>
public sealed class FormsGuardOperationSecurityFilter : BackOfficeSecurityRequirementsOperationFilterBase
{
    protected override string ApiName => FormsGuardApi.Name;
}

/// <summary>Uses the action name as the operation id, so the generated client reads well.</summary>
public sealed class FormsGuardOperationIdHandler : OperationIdHandler
{
    public FormsGuardOperationIdHandler(IOptions<ApiVersioningOptions> apiVersioningOptions)
        : base(apiVersioningOptions)
    {
    }

    protected override bool CanHandle(ApiDescription apiDescription, ControllerActionDescriptor controllerActionDescriptor) =>
        controllerActionDescriptor.ControllerTypeInfo.Namespace?.StartsWith("Cogworks.Umbraco.FormsGuard.Api", StringComparison.Ordinal) is true;

    public override string Handle(ApiDescription apiDescription) =>
        $"{apiDescription.ActionDescriptor.RouteValues["action"]}";
}

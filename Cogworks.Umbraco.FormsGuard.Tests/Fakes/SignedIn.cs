using System.Reflection;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;

namespace Cogworks.Umbraco.FormsGuard.Tests.Fakes;

/// <summary>A backoffice security accessor whose current user has the given key; other members are unexpected.</summary>
public class SignedIn : DispatchProxy
{
    private object? _value;
    private string _member = string.Empty;

    public static IBackOfficeSecurityAccessor Create(Guid userKey)
    {
        var user = Answer<IUser>("get_Key", userKey);
        var security = Answer<IBackOfficeSecurity>("get_CurrentUser", user);
        return Answer<IBackOfficeSecurityAccessor>("get_BackOfficeSecurity", security);
    }

    /// <summary>An accessor with no backoffice security, as for a request with no signed-in user.</summary>
    public static IBackOfficeSecurityAccessor None() => Answer<IBackOfficeSecurityAccessor>("get_BackOfficeSecurity", null);

    private static T Answer<T>(string member, object? value) where T : class
    {
        var proxy = DispatchProxy.Create<T, SignedIn>();
        ((SignedIn)(object)proxy)._member = member;
        ((SignedIn)(object)proxy)._value = value;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name == _member
            ? _value
            : throw new NotSupportedException($"{targetMethod?.DeclaringType?.Name}.{targetMethod?.Name} was not expected");
}

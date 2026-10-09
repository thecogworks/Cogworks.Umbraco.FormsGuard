using System.Reflection;
using Cogworks.Umbraco.FormsGuard.Api;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;

namespace Cogworks.Umbraco.FormsGuard.Tests.Api;

public class UserNameResolverTests
{
    private static readonly Guid KnownKey = Guid.NewGuid();

    [Fact]
    public async Task System_IsFormsGuard()
    {
        var names = await Resolver().ResolveAsync(["system"]);
        Assert.Equal("Forms Guard", names["system"]);
    }

    [Fact]
    public async Task KnownKey_IsUserName()
    {
        var names = await Resolver().ResolveAsync([KnownKey.ToString()]);
        Assert.Equal("Ada Lovelace", names[KnownKey.ToString()]);
    }

    [Fact]
    public async Task UnknownKey_IsItself()
    {
        var unknown = Guid.NewGuid().ToString();
        var names = await Resolver().ResolveAsync([unknown]);
        Assert.Equal(unknown, names[unknown]);
    }

    [Fact]
    public async Task NonGuid_IsItselfAndNullsAreSkipped()
    {
        var names = await Resolver().ResolveAsync(["someone", null, "", "someone"]);
        Assert.Equal("someone", Assert.Single(names).Value);
    }

    [Fact]
    public async Task OneBatchCall_ForManyRows()
    {
        var calls = 0;
        var resolver = new UserNameResolver(UserServiceFake.Create(() => calls++));
        await resolver.ResolveAsync([KnownKey.ToString(), KnownKey.ToString(), Guid.NewGuid().ToString(), "system"]);
        Assert.Equal(1, calls);
    }

    private static UserNameResolver Resolver() => new(UserServiceFake.Create(() => { }));

    /// <summary>Answers <c>GetAsync(IEnumerable&lt;Guid&gt;)</c> with one known user; anything else fails the test.</summary>
    public class UserServiceFake : DispatchProxy
    {
        private Action _onCall = () => { };

        public static IUserService Create(Action onCall)
        {
            var proxy = DispatchProxy.Create<IUserService, UserServiceFake>();
            ((UserServiceFake)(object)proxy)._onCall = onCall;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var parameters = targetMethod?.GetParameters() ?? Array.Empty<ParameterInfo>();
            if (targetMethod?.Name == "GetAsync" && parameters.Length == 1 && parameters[0].ParameterType == typeof(IEnumerable<Guid>))
            {
                _onCall();
                var keys = ((IEnumerable<Guid>)args![0]!).ToList();
                IEnumerable<IUser> users = keys.Contains(KnownKey) ? [UserFake.Create(KnownKey, "Ada Lovelace")] : [];
                return Task.FromResult(users);
            }

            throw new NotSupportedException($"IUserService.{targetMethod?.Name} was not expected");
        }
    }

    /// <summary>Answers <c>Key</c> and <c>Name</c>.</summary>
    public class UserFake : DispatchProxy
    {
        private Guid _key;
        private string _name = string.Empty;

        public static IUser Create(Guid key, string name)
        {
            var proxy = DispatchProxy.Create<IUser, UserFake>();
            var fake = (UserFake)(object)proxy;
            fake._key = key;
            fake._name = name;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_Key" => _key,
            "get_Name" => _name,
            _ => throw new NotSupportedException($"IUser.{targetMethod?.Name} was not expected"),
        };
    }
}

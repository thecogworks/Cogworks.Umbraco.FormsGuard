using System.Reflection;
using Cogworks.Umbraco.FormsGuard.Security;
using Cogworks.Umbraco.FormsGuard.Tests.Api;
using Umbraco.Forms.Core.Security;
using FormsForm = Umbraco.Forms.Core.Models.Form;

namespace Cogworks.Umbraco.FormsGuard.Tests.Security;

public class FormAccessTests
{
    private readonly FormsForm _a = new() { Id = Guid.NewGuid(), Name = "A" };
    private readonly FormsForm _b = new() { Id = Guid.NewGuid(), Name = "B" };

    [Fact]
    public void EntryFormIds_OnlyExistingFormsTheUserCanAccess()
    {
        var deleted = Guid.NewGuid();
        var access = NewAccess(allowed: [_a.Id, deleted], canView: true);

        Assert.Equal([_a.Id], access.EntryFormIds());
        Assert.Equal([_a.Id], access.SettingsFormIds());
    }

    [Fact]
    public void EntryFormIds_NoViewEntriesRight_Empty_SettingsUnaffected()
    {
        var access = NewAccess(allowed: [_a.Id, _b.Id], canView: false);

        Assert.Empty(access.EntryFormIds());
        Assert.Equal(new HashSet<Guid> { _a.Id, _b.Id }, access.SettingsFormIds());
    }

    [Fact]
    public void CanAccessForm_And_CanEditEntries_FollowForms()
    {
        var access = NewAccess(allowed: [_a.Id], canView: true, canEdit: false);

        Assert.True(access.CanAccessForm(_a.Id));
        Assert.False(access.CanAccessForm(_b.Id));
        Assert.False(access.CanEditEntries());
    }

    private FormAccess NewAccess(Guid[] allowed, bool canView, bool canEdit = true) =>
        new(
            FormsSecurityFake.Create(allowed, canView, canEdit),
            SettingsControllerEndpointTests.FormServiceFake.Create([_a, _b]));

    /// <summary>Answers the <see cref="IFormsSecurity"/> members FormAccess uses; anything else fails the test.</summary>
    public class FormsSecurityFake : DispatchProxy
    {
        private HashSet<Guid> _allowed = [];
        private bool _canView;
        private bool _canEdit;

        public static IFormsSecurity Create(IEnumerable<Guid> allowed, bool canView, bool canEdit)
        {
            var proxy = DispatchProxy.Create<IFormsSecurity, FormsSecurityFake>();
            var fake = (FormsSecurityFake)(object)proxy;
            fake._allowed = allowed.ToHashSet();
            fake._canView = canView;
            fake._canEdit = canEdit;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IFormsSecurity.FilterFormIdsForCurrentUser) =>
                ((IEnumerable<Guid>)args![0]!).Where(_allowed.Contains).ToList(),
            nameof(IFormsSecurity.CanCurrentUserViewEntries) => _canView,
            nameof(IFormsSecurity.CanCurrentUserEditEntries) => _canEdit,
            _ => throw new NotSupportedException($"IFormsSecurity.{targetMethod?.Name} was not expected"),
        };
    }
}

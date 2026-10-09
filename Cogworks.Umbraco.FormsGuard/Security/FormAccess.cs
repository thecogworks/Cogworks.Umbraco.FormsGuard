using Umbraco.Forms.Core.Security;
using Umbraco.Forms.Core.Services;

namespace Cogworks.Umbraco.FormsGuard.Security;

/// <summary>
/// The current backoffice user's Umbraco Forms access, as Forms itself answers it. Forms Guard adds no permission
/// layer of its own: entry rights are global per user, form access is per form (including start folders).
/// </summary>
public interface IFormAccess
{
    /// <summary>
    /// Ids of existing forms whose entries the current user may see: forms they can access, and only when they
    /// have the view-entries right. Empty without that right.
    /// </summary>
    IReadOnlySet<Guid> EntryFormIds();

    /// <summary>Ids of existing forms the current user can access, with no entries check.</summary>
    IReadOnlySet<Guid> SettingsFormIds();

    /// <summary>Whether the current user can access this form.</summary>
    bool CanAccessForm(Guid formId);

    /// <summary>Whether the current user has the edit-entries right.</summary>
    bool CanEditEntries();
}

/// <summary>
/// <see cref="IFormAccess"/> over <see cref="IFormsSecurity"/>. Scoped, so each request asks Forms once per question.
/// Never calls <c>CanCurrentUserManageForms</c>, which writes security rows as a side effect.
/// </summary>
public sealed class FormAccess : IFormAccess
{
    private readonly IFormsSecurity _formsSecurity;
    private readonly IFormService _formService;
    private IReadOnlySet<Guid>? _settingsFormIds;
    private IReadOnlySet<Guid>? _entryFormIds;

    public FormAccess(IFormsSecurity formsSecurity, IFormService formService)
    {
        _formsSecurity = formsSecurity;
        _formService = formService;
    }

    public IReadOnlySet<Guid> EntryFormIds() =>
        _entryFormIds ??= _formsSecurity.CanCurrentUserViewEntries()
            ? SettingsFormIds()
            : new HashSet<Guid>();

    public IReadOnlySet<Guid> SettingsFormIds() =>
        _settingsFormIds ??= _formsSecurity
            .FilterFormIdsForCurrentUser(_formService.GetSlim().Select(f => f.Id).ToList())
            .ToHashSet();

    public bool CanAccessForm(Guid formId) => _formsSecurity.FilterFormIdsForCurrentUser([formId]).Any();

    public bool CanEditEntries() => _formsSecurity.CanCurrentUserEditEntries();
}

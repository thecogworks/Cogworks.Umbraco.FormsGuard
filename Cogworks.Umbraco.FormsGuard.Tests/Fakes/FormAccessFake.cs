using Cogworks.Umbraco.FormsGuard.Security;

namespace Cogworks.Umbraco.FormsGuard.Tests.Fakes;

/// <summary>Answers <see cref="IFormAccess"/> from fixed sets and flags.</summary>
public sealed class FormAccessFake : IFormAccess
{
    private readonly HashSet<Guid> _forms;

    public FormAccessFake(IEnumerable<Guid> forms, bool canViewEntries = true, bool canEditEntries = true)
    {
        _forms = forms.ToHashSet();
        CanViewEntries = canViewEntries;
        CanEdit = canEditEntries;
    }

    public bool CanViewEntries { get; }
    public bool CanEdit { get; }

    /// <summary>Full access to the given forms, with both entry rights.</summary>
    public static FormAccessFake AllowAll(params Guid[] forms) => new(forms);

    public IReadOnlySet<Guid> EntryFormIds() => CanViewEntries ? _forms : new HashSet<Guid>();
    public IReadOnlySet<Guid> SettingsFormIds() => _forms;
    public bool CanAccessForm(Guid formId) => _forms.Contains(formId);
    public bool CanEditEntries() => CanEdit;
}

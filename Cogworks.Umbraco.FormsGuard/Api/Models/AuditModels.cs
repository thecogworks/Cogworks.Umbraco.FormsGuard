namespace Cogworks.Umbraco.FormsGuard.Api.Models;

/// <summary>One audit row for the backoffice. Never carries Forms field values.</summary>
public sealed class AuditListItem
{
    public int Id { get; init; }

    public Guid? RecordId { get; init; }

    public Guid? FormId { get; init; }

    /// <summary><c>decision</c>, <c>approve</c>, <c>confirm-spam</c> or <c>restore</c>.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Backoffice user key or <c>system</c>.</summary>
    public string? Actor { get; init; }

    /// <summary>The actor's display name: "Forms Guard" for <c>system</c>, the key itself when the user is gone.</summary>
    public string? ActorName { get; init; }

    public string? Detail { get; init; }

    public DateTime CreatedUtc { get; init; }
}

/// <summary>A page of audit rows plus the total row count.</summary>
public sealed class AuditPagedResult
{
    public long Total { get; init; }

    public IReadOnlyList<AuditListItem> Items { get; init; } = Array.Empty<AuditListItem>();
}

/// <summary>A form the log can be filtered by.</summary>
public sealed class LogFormOption
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
}

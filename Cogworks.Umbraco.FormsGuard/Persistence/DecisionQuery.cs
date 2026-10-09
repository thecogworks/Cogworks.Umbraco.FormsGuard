namespace Cogworks.Umbraco.FormsGuard.Persistence;

/// <summary>
/// Filters for reading decisions. Null parts are not applied. <see cref="FromUtc"/> is inclusive and
/// <see cref="ToUtc"/> exclusive, both compared with <c>CreatedUtc</c>. <see cref="FormIds"/> limits rows to those
/// forms (the forms the current user may see); callers short-circuit an empty set rather than query with it.
/// </summary>
public sealed record DecisionQuery(
    IReadOnlyCollection<string>? Statuses,
    Guid? FormId,
    DateTime? FromUtc,
    DateTime? ToUtc,
    IReadOnlyCollection<Guid>? FormIds = null);

/// <summary>
/// Filters for reading audit rows; same date and <see cref="FormIds"/> rules as <see cref="DecisionQuery"/>.
/// When <see cref="FormIds"/> is set, rows with no form are excluded.
/// </summary>
public sealed record AuditQuery(Guid? FormId, DateTime? FromUtc, DateTime? ToUtc, IReadOnlyCollection<Guid>? FormIds = null);

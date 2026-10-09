namespace Cogworks.Umbraco.FormsGuard.Api.Models;

/// <summary>One decision row for the backoffice. Never carries Forms field values.</summary>
public sealed class DecisionListItem
{
    public int Id { get; init; }

    public Guid RecordId { get; init; }

    public Guid FormId { get; init; }

    public string Status { get; init; } = string.Empty;

    /// <summary><c>rule</c>, <c>provider</c>, <c>policy</c>, <c>record</c> or <c>settings</c> (the form's setup decided, for example no questions to ask).</summary>
    public string? Source { get; init; }

    public string? RuleHit { get; init; }

    public string? Provider { get; init; }

    public string? ModelVersion { get; init; }

    /// <summary>Question key to probability; null when absent or unreadable.</summary>
    public IReadOnlyDictionary<string, double>? Probabilities { get; init; }

    public int Attempts { get; init; }

    public string? Reviewer { get; init; }

    /// <summary>The reviewer's display name; the key itself when the user is gone; null when there is no reviewer.</summary>
    public string? ReviewerName { get; init; }

    public DateTime CreatedUtc { get; init; }

    public DateTime UpdatedUtc { get; init; }
}

/// <summary>A page of decisions plus the total row count.</summary>
public sealed class DecisionPagedResult
{
    public long Total { get; init; }

    /// <summary><c>ApprovedNotChecked</c> rows for the same form and dates, whatever the status filter.</summary>
    public long ApprovedNotCheckedTotal { get; init; }

    public IReadOnlyList<DecisionListItem> Items { get; init; } = Array.Empty<DecisionListItem>();
}

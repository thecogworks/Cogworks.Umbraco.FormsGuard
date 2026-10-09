namespace Cogworks.Umbraco.FormsGuard.Api.Models;

/// <summary>One Forms field on a queued entry, read live from Forms. Never stored or logged by Forms Guard.</summary>
public sealed class ReviewQueueField
{
    public string? Caption { get; init; }

    public string? Value { get; init; }
}

/// <summary>A Review or Quarantined decision with its entry's form name and field values.</summary>
public sealed class ReviewQueueItem
{
    public DecisionListItem Decision { get; init; } = new();

    /// <summary>The form's name; null when the form is gone.</summary>
    public string? FormName { get; init; }

    /// <summary>True when the form or the Forms record could not be read; <see cref="Fields"/> is then empty.</summary>
    public bool RecordMissing { get; init; }

    public IReadOnlyList<ReviewQueueField> Fields { get; init; } = Array.Empty<ReviewQueueField>();
}

/// <summary>A page of the review queue plus the total queued row count.</summary>
public sealed class ReviewQueuePagedResult
{
    public long Total { get; init; }

    public IReadOnlyList<ReviewQueueItem> Items { get; init; } = Array.Empty<ReviewQueueItem>();

    /// <summary>
    /// Whether the user has the Forms edit-entries right, so the queue can hide actions it would refuse. The action
    /// endpoints still check it.
    /// </summary>
    public bool CanEditEntries { get; init; }
}

/// <summary>How a review action ended and the decision's status afterwards (null when unknown).</summary>
public sealed class ReviewActionResponse
{
    /// <summary><c>Done</c>, <c>Refused</c> or <c>Failed</c>.</summary>
    public string Outcome { get; init; } = string.Empty;

    public string? Status { get; init; }
}

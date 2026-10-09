using System.Text.Json;
using Cogworks.Umbraco.FormsGuard.Api.Models;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Microsoft.Extensions.Logging;

namespace Cogworks.Umbraco.FormsGuard.Api;

/// <summary>Paging, row mapping and UTC helpers shared by the Forms Guard API controllers.</summary>
public static class DecisionMapping
{
    public const int MaxTake = 100;

    /// <summary>Floors <paramref name="skip"/> at 0 and clamps <paramref name="take"/> to 1..<see cref="MaxTake"/>.</summary>
    public static (int Skip, int Take) ClampPaging(int skip, int take) =>
        (Math.Max(0, skip), Math.Clamp(take, 1, MaxTake));

    public static DecisionListItem ToListItem(DecisionDto row, ILogger logger, string? reviewerName = null) => new()
    {
        Id = row.Id,
        RecordId = row.RecordId,
        FormId = row.FormId,
        Status = row.Status,
        Source = row.Source,
        RuleHit = row.RuleHit,
        Provider = row.Provider,
        ModelVersion = row.ModelVersion,
        Probabilities = ParseProbabilities(row, logger),
        Attempts = row.Attempts,
        Reviewer = row.Reviewer,
        ReviewerName = reviewerName,
        CreatedUtc = AsUtc(row.CreatedUtc),
        UpdatedUtc = AsUtc(row.UpdatedUtc),
    };

    /// <summary>
    /// Treats an unspecified kind as UTC and converts a local time to UTC. NPoco returns Kind=Unspecified, so this
    /// marks stored times as UTC and JSON carries the Z suffix.
    /// </summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <inheritdoc cref="AsUtc(DateTime)"/>
    public static DateTime? AsUtc(DateTime? value) => value is { } v ? AsUtc(v) : null;

    private static IReadOnlyDictionary<string, double>? ParseProbabilities(DecisionDto row, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(row.Probabilities))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, double>>(row.Probabilities);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "FormsGuard: decision {DecisionId} has unreadable probabilities JSON", row.Id);
            return null;
        }
    }
}

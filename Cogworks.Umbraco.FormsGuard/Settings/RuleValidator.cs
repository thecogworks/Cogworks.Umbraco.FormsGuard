using System.Text.RegularExpressions;
using Cogworks.Umbraco.FormsGuard.Persistence.Dtos;
using Cogworks.Umbraco.FormsGuard.Processing;

namespace Cogworks.Umbraco.FormsGuard.Settings;

/// <summary>Validates a new hard rule against the form's existing rules. Returns one error per problem.</summary>
public static partial class RuleValidator
{
    public const int MaxRulesPerForm = 500;
    public const int DomainMaxLength = 253;
    public const int PhraseMaxLength = 200;

    [GeneratedRegex(@"^[a-z0-9-]+(\.[a-z0-9-]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex DomainPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    public static IReadOnlyList<string> Validate(string? ruleType, string? pattern, IReadOnlyList<RuleDto> existing)
    {
        var errors = new List<string>();

        if (existing.Count >= MaxRulesPerForm)
        {
            errors.Add($"A form can have at most {MaxRulesPerForm} rules.");
        }

        var typeKnown = HardRules.TryParse(ruleType, out var type);
        if (!typeKnown)
        {
            errors.Add("Rule type is not recognised; use BlockedDomain, AllowedDomain or BlockedPhrase.");
        }

        var trimmed = pattern?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            errors.Add("Pattern is required.");
            return errors;
        }

        if (trimmed.Any(char.IsControl))
        {
            errors.Add("Pattern contains control characters.");
            return errors;
        }

        if (!typeKnown)
        {
            return errors;
        }

        var key = Normalise(type, trimmed);
        if (type == HardRuleType.BlockedPhrase)
        {
            if (trimmed.Length > PhraseMaxLength)
            {
                errors.Add($"Phrase must be at most {PhraseMaxLength} characters.");
            }
        }
        else if (key is not null && key.Length > DomainMaxLength)
        {
            // Measured after a leading '@' or '*.' is stripped.
            errors.Add($"Domain must be at most {DomainMaxLength} characters.");
            return errors;
        }
        else if (key is null || !DomainPattern().IsMatch(key))
        {
            errors.Add("Pattern must be a domain, such as spam.example, @spam.example or *.spam.example.");
            return errors;
        }

        var duplicate = existing.Any(r =>
            HardRules.TryParse(r.RuleType, out var existingType)
            && existingType == type
            && string.Equals(Normalise(existingType, r.Pattern ?? string.Empty), key, StringComparison.Ordinal));
        if (duplicate)
        {
            errors.Add("An identical rule already exists for this form.");
        }

        return errors;
    }

    /// <summary>The comparison key: normalised domain, or the phrase lowercased with whitespace collapsed.</summary>
    private static string? Normalise(HardRuleType type, string pattern) =>
        type == HardRuleType.BlockedPhrase
            ? Whitespace().Replace(pattern.Trim(), " ").ToLowerInvariant()
            : HardRules.NormaliseDomain(pattern);
}

using System.Text.RegularExpressions;
using Cogworks.Umbraco.FormsGuard.Decisions;

namespace Cogworks.Umbraco.FormsGuard.Processing;

/// <summary>The kinds of exact rule that can decide an entry without a provider call.</summary>
public enum HardRuleType
{
    BlockedDomain,
    AllowedDomain,
    BlockedPhrase,
}

/// <summary>One per-form hard rule.</summary>
public sealed record HardRule(int Id, HardRuleType Type, string Pattern);

/// <summary>The rule that decided an entry. <see cref="ToString"/> gives <c>Type:Id</c>.</summary>
public sealed record RuleHit(DecisionStatus Status, HardRuleType Type, int RuleId)
{
    public override string ToString() => $"{Type}:{RuleId}";
}

/// <summary>
/// Pure engine for hard rules over the full record. Blocked domains, then blocked phrases, then allowed
/// domains, each in rule id order; the first hit wins. Logs nothing.
/// </summary>
public static class HardRules
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    public static bool TryParse(string? ruleType, out HardRuleType type)
    {
        type = default;
        var trimmed = ruleType?.Trim();
        foreach (var candidate in Enum.GetValues<HardRuleType>())
        {
            if (string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                type = candidate;
                return true;
            }
        }

        return false;
    }

    public static RuleHit? Evaluate(IReadOnlyList<HardRule> rules, IReadOnlyList<StateField> fields, Guid? emailFieldId)
    {
        var ordered = rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Pattern))
            .OrderBy(r => r.Id)
            .ToList();
        if (ordered.Count == 0)
        {
            return null;
        }

        var domains = CandidateDomains(fields, emailFieldId);

        foreach (var rule in ordered.Where(r => r.Type == HardRuleType.BlockedDomain))
        {
            var pattern = NormaliseDomain(rule.Pattern);
            if (pattern is not null && domains.Any(d => DomainMatches(d, pattern)))
            {
                return new RuleHit(DecisionStatus.Quarantined, rule.Type, rule.Id);
            }
        }

        var phraseRules = ordered.Where(r => r.Type == HardRuleType.BlockedPhrase).ToList();
        if (phraseRules.Count > 0)
        {
            var values = fields
                .Where(f => !DecisionStateBuilder.IsExcludedType(f.FieldTypeId) && !string.IsNullOrWhiteSpace(f.Value))
                .Select(f => f.Value!)
                .ToList();

            foreach (var rule in phraseRules)
            {
                var regex = PhraseRegex(rule.Pattern);
                if (regex is not null && values.Any(v => SafeIsMatch(regex, v)))
                {
                    return new RuleHit(DecisionStatus.Quarantined, rule.Type, rule.Id);
                }
            }
        }

        var distinct = domains.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count == 1)
        {
            foreach (var rule in ordered.Where(r => r.Type == HardRuleType.AllowedDomain))
            {
                var pattern = NormaliseDomain(rule.Pattern);
                if (pattern is not null && DomainMatches(distinct[0], pattern))
                {
                    return new RuleHit(DecisionStatus.Approved, rule.Type, rule.Id);
                }
            }
        }

        return null;
    }

    private static List<string> CandidateDomains(IReadOnlyList<StateField> fields, Guid? emailFieldId)
    {
        var result = new List<string>();
        emailFieldId = DecisionStateBuilder.ExistingFieldId(emailFieldId, fields.Select(f => f.Id));
        foreach (var field in fields)
        {
            var isCandidate = emailFieldId is { } id
                ? field.Id == id && !DecisionStateBuilder.IsExcludedType(field.FieldTypeId)
                : field.LooksLikeEmail && !DecisionStateBuilder.IsExcludedType(field.FieldTypeId);
            if (!isCandidate)
            {
                continue;
            }

            var domain = DecisionStateBuilder.DomainOf(field.Value);
            if (domain is not null)
            {
                result.Add(domain);
            }
        }

        return result;
    }

    internal static string? NormaliseDomain(string pattern)
    {
        var p = pattern.Trim().ToLowerInvariant();
        if (p.StartsWith('@'))
        {
            p = p[1..];
        }
        else if (p.StartsWith("*.", StringComparison.Ordinal))
        {
            p = p[2..];
        }

        p = p.Trim();
        return p.Length == 0 ? null : p;
    }

    private static bool DomainMatches(string domain, string pattern) =>
        domain == pattern || domain.EndsWith("." + pattern, StringComparison.Ordinal);

    private static Regex? PhraseRegex(string phrase)
    {
        var words = phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return null;
        }

        var body = string.Join(@"\s+", words.Select(Regex.Escape));
        return new Regex(
            @"(?<![\p{L}\p{N}])" + body + @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            MatchTimeout);
    }

    private static bool SafeIsMatch(Regex regex, string value)
    {
        try
        {
            return regex.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}

using System.Text.RegularExpressions;

namespace Cogworks.Umbraco.FormsGuard.Decisions;

/// <summary>The one question-key pattern: <c>namespace.name</c>, lowercase letters, digits and underscores.</summary>
public static partial class QuestionKeys
{
    /// <summary>The namespace reserved for core (per-form settings) questions.</summary>
    public const string GuardPrefix = "guard.";

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,31}\.[a-z][a-z0-9_]{0,48}\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    /// <summary>True when the key is a valid namespaced key.</summary>
    public static bool IsValid(string? key) => key is not null && KeyPattern().IsMatch(key);

    /// <summary>True when the key is valid and in the <c>guard.</c> namespace.</summary>
    public static bool IsGuardKey(string? key) => IsValid(key) && key!.StartsWith(GuardPrefix, StringComparison.Ordinal);
}

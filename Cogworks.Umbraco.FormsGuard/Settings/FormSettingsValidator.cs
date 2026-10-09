using Cogworks.Umbraco.FormsGuard.Decisions;

namespace Cogworks.Umbraco.FormsGuard.Settings;

/// <summary>Validates administrator-written per-form settings. Returns one error per problem.</summary>
public static class FormSettingsValidator
{
    public const int OrganisationMaxLength = 1000;
    public const int QuestionTextMaxLength = 500;

    public const int MaxQuestions = 20;

    /// <summary>The error for a custom allowlist with no fields.</summary>
    public const string EmptyAllowlistError = "A custom field allowlist must include at least one field; use the default fields instead.";

    /// <summary>
    /// <see cref="Validate"/> plus the checks that apply only to a new save: a custom allowlist must not be empty.
    /// Stored rows are read with <see cref="Validate"/> alone, so an already-saved empty allowlist keeps working as before.
    /// </summary>
    public static IReadOnlyList<string> ValidateForSave(FormGuardSettings settings)
    {
        var errors = new List<string>();
        if (settings.AllowedFieldIds is { Count: 0 })
        {
            errors.Add(EmptyAllowlistError);
        }

        errors.AddRange(Validate(settings));
        return errors;
    }

    public static IReadOnlyList<string> Validate(FormGuardSettings settings)
    {
        var errors = new List<string>();

        var organisation = settings.Organisation ?? string.Empty;
        if (organisation.Length > OrganisationMaxLength)
        {
            errors.Add($"Organisation must be at most {OrganisationMaxLength} characters.");
        }

        if (HasDisallowedControlChars(organisation))
        {
            errors.Add("Organisation contains control characters.");
        }

        var questions = settings.Questions ?? Array.Empty<QuestionSetting>();
        if (questions.Count > MaxQuestions)
        {
            errors.Add($"There must be at most {MaxQuestions} questions.");
        }

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < questions.Count; i++)
        {
            var question = questions[i];
            var label = $"Question {i + 1}";
            if (question is null)
            {
                errors.Add($"{label} is missing.");
                continue;
            }

            var key = question.Key ?? string.Empty;
            if (!QuestionKeys.IsGuardKey(key))
            {
                errors.Add($"{label} key must match 'guard.' followed by lowercase letters, digits or underscores (max 49 after 'guard.').");
            }
            else if (!seenKeys.Add(key))
            {
                errors.Add($"{label} key '{key}' is a duplicate.");
            }

            var text = question.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                errors.Add($"{label} text is required.");
            }
            else if (text.Length > QuestionTextMaxLength)
            {
                errors.Add($"{label} text must be at most {QuestionTextMaxLength} characters.");
            }

            if (HasDisallowedControlChars(text))
            {
                errors.Add($"{label} text contains control characters.");
            }

            CheckCriteria(question.TrueCriteria, $"{label} true criteria", errors);
            CheckCriteria(question.FalseCriteria, $"{label} false criteria", errors);

            if (!Enum.IsDefined(question.Role))
            {
                errors.Add($"{label} role is not recognised.");
            }
        }

        var thresholds = settings.Thresholds;
        if (thresholds is null)
        {
            errors.Add("Thresholds are required.");
        }
        else
        {
            CheckUnit(thresholds.QuarantineSpamMin, "Quarantine spam minimum", errors);
            CheckUnit(thresholds.ApproveSpamMax, "Approve spam maximum", errors);
            CheckUnit(thresholds.ApproveGenuineMin, "Approve genuine minimum", errors);
            if (!(thresholds.ApproveSpamMax < thresholds.QuarantineSpamMin))
            {
                errors.Add("Approve spam maximum must be lower than quarantine spam minimum.");
            }
        }

        if (!Enum.IsDefined(settings.FailurePolicy))
        {
            errors.Add("Failure policy is not recognised.");
        }

        return errors;
    }

    private static void CheckCriteria(string? value, string name, List<string> errors)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > QuestionTextMaxLength)
        {
            errors.Add($"{name} must be at most {QuestionTextMaxLength} characters.");
        }

        if (HasDisallowedControlChars(value))
        {
            errors.Add($"{name} contains control characters.");
        }
    }

    private static void CheckUnit(double value, string name, List<string> errors)
    {
        if (!(value >= 0 && value <= 1))
        {
            errors.Add($"{name} must be between 0 and 1.");
        }
    }

    private static bool HasDisallowedControlChars(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')
            {
                return true;
            }
        }

        return false;
    }
}

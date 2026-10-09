using System.Text.RegularExpressions;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Settings;
using Umbraco.Forms.Core.Models;
using Umbraco.Forms.Core.Persistence.Dtos;
using FormsConstants = Umbraco.Forms.Core.Constants;

namespace Cogworks.Umbraco.FormsGuard.Processing;

/// <summary>One form field with its submitted value, decoupled from Forms types.</summary>
/// <param name="Id">The Forms field id.</param>
/// <param name="Caption">The field caption.</param>
/// <param name="Alias">The field alias.</param>
/// <param name="FieldTypeId">The Forms field type id.</param>
/// <param name="LooksLikeEmail">True when the field is an email candidate.</param>
/// <param name="Value">The submitted value, or <c>null</c> when none.</param>
public sealed record StateField(
    Guid Id,
    string? Caption,
    string? Alias,
    Guid FieldTypeId,
    bool LooksLikeEmail,
    string? Value);

/// <summary>
/// Builds the minimum-data <see cref="DecisionState"/> sent to the provider: only allowlisted fields,
/// email local parts masked, no uploads or passwords, values trimmed and truncated. Logs nothing.
/// </summary>
public static partial class DecisionStateBuilder
{
    public const int MaxFieldLength = 4000;

    // Slack kept before masking so an address straddling the final cut is still masked.
    private const int MaskSlack = 512;

    private static readonly Guid TextareaType = ToGuid(FormsConstants.FieldTypes.Textarea);
    private static readonly Guid UploadType = ToGuid(FormsConstants.FieldTypes.Upload);
    private static readonly Guid PasswordType = ToGuid(FormsConstants.FieldTypes.Password);

    private static readonly string[] DefaultCaptions = { "Subject", "Company" };

    [GeneratedRegex(@"^[^@\s]+@([A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidEmail();

    [GeneratedRegex(@"[^\s<>()]+@(?=[^\s@<>()])", RegexOptions.CultureInvariant)]
    private static partial Regex EmailLocalPart();

    public static DecisionState Build(FormGuardSettings settings, string formName, IReadOnlyList<StateField> fields)
    {
        var allowed = settings.AllowedFieldIds is null ? null : new HashSet<Guid>(settings.AllowedFieldIds);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields)
        {
            if (IsExcludedType(field.FieldTypeId))
            {
                continue;
            }

            var include = allowed is null ? IsDefaultAllowed(field) : allowed.Contains(field.Id);
            if (!include)
            {
                continue;
            }

            var value = CleanValue(field.Value);
            if (value is null)
            {
                continue;
            }

            map[UniqueKey(map, KeyFor(field))] = value;
        }

        var emailDomain = settings.SendEmailDomain ? FindEmailDomain(settings.EmailFieldId, fields) : null;

        return new DecisionState(
            Organisation: settings.Organisation ?? string.Empty,
            Form: formName ?? string.Empty,
            EmailDomain: emailDomain,
            Fields: map);
    }

    /// <summary>Maps a Forms form and record onto <see cref="StateField"/>s, in field order.</summary>
    public static IReadOnlyList<StateField> FromRecord(Form form, Record record)
    {
        // RecordFields is keyed by the record field's own key, not the form field id, so index by FieldId.
        var byFieldId = new Dictionary<Guid, RecordField>();
        foreach (var recordField in record.RecordFields.Values)
        {
            if (recordField is not null)
            {
                byFieldId.TryAdd(recordField.FieldId, recordField);
            }
        }

        var result = new List<StateField>();
        foreach (var field in form.AllFields)
        {
            string? value = null;
            if (byFieldId.TryGetValue(field.Id, out var recordField))
            {
                value = recordField.ValuesAsString(false);
            }

            result.Add(new StateField(
                field.Id,
                field.Caption,
                field.Alias,
                field.FieldTypeId,
                LooksLikeEmail(field.Caption, field.Alias, field.RegEx, field.FieldTypeId),
                value));
        }

        return result;
    }

    internal static bool LooksLikeEmail(string? caption, string? alias, string? regEx, Guid fieldTypeId)
    {
        if (IsExcludedType(fieldTypeId))
        {
            return false;
        }

        return MentionsEmail(caption) || MentionsEmail(alias) || (regEx?.Contains('@') ?? false);
    }

    private static bool MentionsEmail(string? text) =>
        text is not null
        && (text.Contains("email", StringComparison.OrdinalIgnoreCase)
            || text.Contains("e-mail", StringComparison.OrdinalIgnoreCase));

    internal static bool IsExcludedType(Guid fieldTypeId) => fieldTypeId == UploadType || fieldTypeId == PasswordType;

    private static bool IsDefaultAllowed(StateField field)
    {
        if (field.FieldTypeId == TextareaType)
        {
            return true;
        }

        var caption = field.Caption?.Trim();
        return caption is not null
            && DefaultCaptions.Any(c => string.Equals(c, caption, StringComparison.OrdinalIgnoreCase));
    }

    private static string? CleanValue(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var cut = Truncate(value, MaxFieldLength + MaskSlack);
        if (cut.Length < value.Length)
        {
            // If the token cut in two reaches an '@' later on, its kept part may be a local part: drop it.
            var start = cut.Length;
            while (start > 0 && !char.IsWhiteSpace(cut[start - 1]))
            {
                start--;
            }

            var end = cut.Length;
            while (end < value.Length && !char.IsWhiteSpace(value[end]))
            {
                end++;
            }

            if (value.IndexOf('@', start, end - start) >= 0)
            {
                cut = cut[..start].TrimEnd();
                if (cut.Length == 0)
                {
                    return null;
                }
            }
        }

        value = EmailLocalPart().Replace(cut, "[email]@");
        return Truncate(value, MaxFieldLength);
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
        {
            return value;
        }

        var length = char.IsHighSurrogate(value[max - 1]) ? max - 1 : max;
        return value[..length];
    }

    private static string KeyFor(StateField field)
    {
        var caption = field.Caption?.Trim();
        if (!string.IsNullOrEmpty(caption))
        {
            return caption;
        }

        var alias = field.Alias?.Trim();
        return string.IsNullOrEmpty(alias) ? field.Id.ToString() : alias;
    }

    private static string UniqueKey(Dictionary<string, string> map, string key)
    {
        if (!map.ContainsKey(key))
        {
            return key;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{key} ({n})";
            if (!map.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// The email field override when a field with that id is on the form, else null (auto-detect). A stale override
    /// left behind by a deleted field must never silence email detection.
    /// </summary>
    public static Guid? ExistingFieldId(Guid? emailFieldId, IEnumerable<Guid> fieldIds) =>
        emailFieldId is { } id && fieldIds.Contains(id) ? id : null;

    private static string? FindEmailDomain(Guid? emailFieldId, IReadOnlyList<StateField> fields)
    {
        if (ExistingFieldId(emailFieldId, fields.Select(f => f.Id)) is { } id)
        {
            var chosen = fields.FirstOrDefault(f => f.Id == id && !IsExcludedType(f.FieldTypeId));
            return chosen is null ? null : DomainOf(chosen.Value);
        }

        string? address = null;
        string? domain = null;
        foreach (var field in fields)
        {
            if (!field.LooksLikeEmail || IsExcludedType(field.FieldTypeId))
            {
                continue;
            }

            var value = field.Value?.Trim();
            var candidateDomain = DomainOf(value);
            if (candidateDomain is null)
            {
                continue;
            }

            if (address is null)
            {
                address = value;
                domain = candidateDomain;
            }
            else if (!string.Equals(address, value, StringComparison.OrdinalIgnoreCase))
            {
                return null; // Differing candidates: never guess.
            }
        }

        return domain;
    }

    internal static string? DomainOf(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var match = ValidEmail().Match(trimmed);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static Guid ToGuid(object id) => id is Guid g ? g : Guid.Parse(id.ToString()!);
}

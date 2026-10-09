using System.Text.Json;
using System.Text.Json.Serialization;
using Cogworks.Umbraco.FormsGuard.Decisions;

namespace Cogworks.Umbraco.FormsGuard.Settings;

/// <summary>
/// Reads and writes the per-form settings JSON. Missing or empty members take shipped defaults;
/// malformed or invalid JSON falls back wholly to defaults. Never throws when parsing.
/// </summary>
public static class FormSettingsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    /// <summary>Parses stored settings. <c>Valid</c> is false when the JSON was malformed or failed validation.</summary>
    public static (FormGuardSettings Settings, bool Valid) Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (DefaultFormSettings.Create(), true);
        }

        try
        {
            var dto = JsonSerializer.Deserialize<SettingsDto>(json, Options);
            if (dto is null)
            {
                return (DefaultFormSettings.Create(), true);
            }

            var settings = FromDto(dto);
            if (settings is null || FormSettingsValidator.Validate(settings).Count > 0)
            {
                return (DefaultFormSettings.Create(), false);
            }

            return (settings, true);
        }
        catch (Exception)
        {
            return (DefaultFormSettings.Create(), false);
        }
    }

    /// <summary>Writes settings in the stored JSON shape.</summary>
    public static string Serialize(FormGuardSettings settings)
    {
        var dto = new SettingsDto
        {
            Organisation = settings.Organisation,
            AllowedFieldIds = settings.AllowedFieldIds?.ToList(),
            SendEmailDomain = settings.SendEmailDomain,
            Questions = settings.Questions
                .Select(q => new QuestionDto
                {
                    Key = q.Key,
                    Text = q.Text,
                    Role = q.Role,
                    Enabled = q.Enabled,
                    TrueCriteria = q.TrueCriteria,
                    FalseCriteria = q.FalseCriteria,
                })
                .ToList<QuestionDto?>(),
            Thresholds = new ThresholdsDto
            {
                QuarantineSpamMin = settings.Thresholds.QuarantineSpamMin,
                ApproveSpamMax = settings.Thresholds.ApproveSpamMax,
                ApproveGenuineMin = settings.Thresholds.ApproveGenuineMin,
            },
            FailurePolicy = settings.FailurePolicy,
            EmailFieldId = settings.EmailFieldId,
        };

        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Maps the nullable DTO onto the model, or null when a stored question is incomplete.</summary>
    private static FormGuardSettings? FromDto(SettingsDto dto)
    {
        var defaults = DefaultFormSettings.Create();

        IReadOnlyList<QuestionSetting> questions;
        if (dto.Questions is null || dto.Questions.Count == 0)
        {
            questions = defaults.Questions;
        }
        else
        {
            var list = new List<QuestionSetting>(dto.Questions.Count);
            foreach (var q in dto.Questions)
            {
                if (q?.Key is null || q.Text is null || q.Role is null)
                {
                    return null;
                }

                list.Add(new QuestionSetting(
                    q.Key, q.Text, q.Role.Value, q.Enabled ?? true, q.TrueCriteria, q.FalseCriteria));
            }

            questions = list;
        }

        var thresholds = new DecisionThresholds(
            dto.Thresholds?.QuarantineSpamMin ?? defaults.Thresholds.QuarantineSpamMin,
            dto.Thresholds?.ApproveSpamMax ?? defaults.Thresholds.ApproveSpamMax,
            dto.Thresholds?.ApproveGenuineMin ?? defaults.Thresholds.ApproveGenuineMin);

        return new FormGuardSettings(
            Organisation: dto.Organisation ?? defaults.Organisation,
            AllowedFieldIds: dto.AllowedFieldIds,
            SendEmailDomain: dto.SendEmailDomain ?? defaults.SendEmailDomain,
            Questions: questions,
            Thresholds: thresholds,
            FailurePolicy: dto.FailurePolicy ?? defaults.FailurePolicy,
            EmailFieldId: dto.EmailFieldId);
    }

    private sealed class SettingsDto
    {
        public string? Organisation { get; set; }

        public List<Guid>? AllowedFieldIds { get; set; }

        public bool? SendEmailDomain { get; set; }

        public List<QuestionDto?>? Questions { get; set; }

        public ThresholdsDto? Thresholds { get; set; }

        public FailurePolicy? FailurePolicy { get; set; }

        public Guid? EmailFieldId { get; set; }
    }

    private sealed class QuestionDto
    {
        public string? Key { get; set; }

        public string? Text { get; set; }

        public QuestionRole? Role { get; set; }

        public bool? Enabled { get; set; }

        public string? TrueCriteria { get; set; }

        public string? FalseCriteria { get; set; }
    }

    private sealed class ThresholdsDto
    {
        public double? QuarantineSpamMin { get; set; }

        public double? ApproveSpamMax { get; set; }

        public double? ApproveGenuineMin { get; set; }
    }
}

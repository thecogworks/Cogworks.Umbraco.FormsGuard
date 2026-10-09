using System.Reflection;
using Cogworks.Umbraco.FormsGuard.Api;
using Cogworks.Umbraco.FormsGuard.Api.Models;
using Cogworks.Umbraco.FormsGuard.Security;
using Cogworks.Umbraco.FormsGuard.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cogworks.Umbraco.FormsGuard.Tests.Api;

public class SettingsControllerTests
{
    private static readonly Guid FieldA = Guid.NewGuid();
    private static readonly Guid FieldB = Guid.NewGuid();
    private static readonly IReadOnlySet<Guid> FormFields = new HashSet<Guid> { FieldA, FieldB };

    [Fact]
    public void Defaults_RoundTripThroughTryMap()
    {
        var defaults = DefaultFormSettings.Create();

        Assert.True(SettingsController.TryMap(SettingsController.ToModel(defaults), FormFields, out var settings, out var errors));
        Assert.Empty(errors);
        Assert.Equal(FormSettingsSerializer.Serialize(defaults), FormSettingsSerializer.Serialize(settings));
    }

    [Fact]
    public void FieldsOnTheForm_AndChangedThresholds_RoundTrip()
    {
        var model = Valid() with
        {
            AllowedFieldIds = new[] { FieldA, FieldA },
            EmailFieldId = FieldB,
            Thresholds = new ThresholdsModel { QuarantineSpamMin = 0.5, ApproveSpamMax = 0.1, ApproveGenuineMin = 0.7 },
            FailurePolicy = "review",
        };

        Assert.True(SettingsController.TryMap(model, FormFields, out var settings, out _));
        Assert.Equal(new[] { FieldA }, settings.AllowedFieldIds);
        Assert.Equal(FieldB, settings.EmailFieldId);
        Assert.Equal(0.5, settings.Thresholds.QuarantineSpamMin);
        Assert.Equal(FailurePolicy.Review, settings.FailurePolicy);

        // Stored and read back, the settings are the same (no silent fallback to defaults).
        var (parsed, valid) = FormSettingsSerializer.Parse(FormSettingsSerializer.Serialize(settings));
        Assert.True(valid);
        Assert.Equal(FormSettingsSerializer.Serialize(settings), FormSettingsSerializer.Serialize(parsed));
    }

    public static TheoryData<string, FormSettingsModel?> InvalidModels() => new()
    {
        { "missing settings", null },
        { "threshold 1.5", Valid() with { Thresholds = Thresholds(1.5, 0.15, 0.7) } },
        { "approve max >= quarantine min", Valid() with { Thresholds = Thresholds(0.5, 0.5, 0.7) } },
        { "organisation over 1000", Valid() with { Organisation = new string('a', 1001) } },
        { "missing organisation", Valid() with { Organisation = null } },
        { "bad key", Valid() with { Questions = new[] { Question() with { Key = "acme.x" } } } },
        { "unknown role", Valid() with { Questions = new[] { Question() with { Role = "Spammy" } } } },
        { "numeric role", Valid() with { Questions = new[] { Question() with { Role = "0" } } } },
        { "unknown policy", Valid() with { FailurePolicy = "Reject" } },
        { "numeric policy", Valid() with { FailurePolicy = "1" } },
        { "missing policy", Valid() with { FailurePolicy = null } },
        { "missing thresholds", Valid() with { Thresholds = null } },
        { "missing one threshold", Valid() with { Thresholds = new ThresholdsModel { QuarantineSpamMin = 0.8, ApproveSpamMax = 0.1 } } },
        { "missing questions", Valid() with { Questions = null } },
        { "empty questions", Valid() with { Questions = Array.Empty<QuestionModel?>() } },
        { "null question", Valid() with { Questions = new QuestionModel?[] { null } } },
        { "question missing enabled", Valid() with { Questions = new[] { Question() with { Enabled = null } } } },
        { "21 questions", Valid() with { Questions = Enumerable.Range(0, 21).Select(i => (QuestionModel?)(Question() with { Key = $"guard.q{i}" })).ToArray() } },
        { "allowed field not on form", Valid() with { AllowedFieldIds = new[] { Guid.NewGuid() } } },
        { "email field not on form", Valid() with { EmailFieldId = Guid.NewGuid() } },
        { "missing send email domain", Valid() with { SendEmailDomain = null } },
    };

    [Theory]
    [MemberData(nameof(InvalidModels))]
    public void InvalidSettings_AreRejectedWithErrors(string scenario, FormSettingsModel? model)
    {
        Assert.False(SettingsController.TryMap(model, FormFields, out var settings, out var errors), scenario);
        Assert.Null(settings);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void SeveralProblems_AreAllListed()
    {
        var model = Valid() with
        {
            FailurePolicy = "Reject",
            EmailFieldId = Guid.NewGuid(),
            Organisation = null,
            Thresholds = Thresholds(1.5, 0.15, 0.7),
            Questions = new[]
            {
                Question() with { Role = "Spammy" },
                Question() with { Key = "acme.x", Text = null },
            },
        };

        Assert.False(SettingsController.TryMap(model, FormFields, out _, out var errors));

        // Structural: policy, email field, organisation, Q1 role, Q2 text. Validator: threshold, Q2 key.
        Assert.Equal(7, errors.Count);
        Assert.Contains("Quarantine spam minimum must be between 0 and 1.", errors);
        Assert.Contains(errors, e => e.StartsWith("Question 2 key", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(nameof(SettingsController.GetForms))]
    [InlineData(nameof(SettingsController.GetFormSettings))]
    [InlineData(nameof(SettingsController.SaveFormSettings))]
    [InlineData(nameof(SettingsController.GetRules))]
    [InlineData(nameof(SettingsController.CreateRule))]
    [InlineData(nameof(SettingsController.DeleteRule))]
    public void Endpoints_RequireManageSettingsPolicy(string action)
    {
        var policies = typeof(SettingsController).GetMethod(action)!
            .GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Policy);
        Assert.Contains(FormsGuardPermissions.ManageSettingsPolicy, policies);
    }

    [Fact]
    public void Controller_HasSixEndpoints_InSettingsGroup()
    {
        var actions = typeof(SettingsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(6, actions.Length);
        Assert.Equal("Settings", typeof(SettingsController).GetCustomAttribute<ApiExplorerSettingsAttribute>()!.GroupName);
    }

    private static FormSettingsModel Valid() => SettingsController.ToModel(DefaultFormSettings.Create());

    private static QuestionModel Question() =>
        new() { Key = "guard.only", Text = "Is this spam?", Role = "SpamSignal", Enabled = true };

    private static ThresholdsModel Thresholds(double q, double a, double g) =>
        new() { QuarantineSpamMin = q, ApproveSpamMax = a, ApproveGenuineMin = g };
}

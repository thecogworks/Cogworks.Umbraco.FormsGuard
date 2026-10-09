using System.Diagnostics;
using Cogworks.Umbraco.FormsGuard.Decisions;
using Cogworks.Umbraco.FormsGuard.Processing;
using Cogworks.Umbraco.FormsGuard.Settings;
using Microsoft.AspNetCore.Mvc;
using FormsConstants = Umbraco.Forms.Core.Constants;

namespace AiFormsGuard.TestSite.SmokeTests;

/// <summary>
/// Development-only probe used by the evaluation CLI: runs one submission through a named provider using the real
/// state builder, default questions and thresholds, and decision rule. Returns 404 outside Development.
/// </summary>
[ApiController]
[Route("/api/forms-guard/evaluate")]
public sealed class EvaluationController : ControllerBase
{
    private static readonly Guid NameFieldId = Guid.Parse("8d1d0f0e-5b0a-4c39-9a51-6f1f6d6a0001");
    private static readonly Guid EmailFieldId = Guid.Parse("8d1d0f0e-5b0a-4c39-9a51-6f1f6d6a0002");
    private static readonly Guid MessageFieldId = Guid.Parse("8d1d0f0e-5b0a-4c39-9a51-6f1f6d6a0003");
    private static readonly Guid TextboxType = ToGuid(FormsConstants.FieldTypes.Textfield);
    private static readonly Guid TextareaType = ToGuid(FormsConstants.FieldTypes.Textarea);

    private readonly IWebHostEnvironment _environment;
    private readonly IEnumerable<IDecisionProvider> _providers;

    public EvaluationController(IWebHostEnvironment environment, IEnumerable<IDecisionProvider> providers)
    {
        _environment = environment;
        _providers = providers;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] EvaluateRequest body, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var settings = DefaultFormSettings.Create();
        var questions = settings.Questions
            .Where(q => q.Enabled)
            .Select(q => new DecisionQuestion(
                q.Key, q.Text, q.Role, QuestionType.Noul, TrueCriteria: q.TrueCriteria, FalseCriteria: q.FalseCriteria))
            .ToList();

        var fields = new[]
        {
            new StateField(NameFieldId, "Name", "name", TextboxType, false, body.Name),
            new StateField(EmailFieldId, "Email", "email", TextboxType, true, body.Email),
            new StateField(MessageFieldId, "Message", "message", TextareaType, false, body.Message),
        };
        var state = DecisionStateBuilder.Build(settings, "Contact", fields);

        var stopwatch = Stopwatch.StartNew();
        DecisionResult result;
        try
        {
            var provider = DecisionProviders.Select(_providers, body.Provider);
            result = await provider.DecideAsync(new DecisionRequest(Guid.Empty, state, questions), cancellationToken);
        }
        catch (Exception ex)
        {
            result = DecisionResult.Failed($"{ex.GetType().Name}: {ex.Message}", retryable: false);
        }

        stopwatch.Stop();

        if (!result.Success)
        {
            return Ok(new EvaluateResponse(false, result.Error, null, null, null, stopwatch.ElapsedMilliseconds, null, null));
        }

        var band = DecisionRule.Evaluate(questions, result.Answers, settings.Thresholds);
        return Ok(new EvaluateResponse(
            true,
            null,
            band.ToString(),
            MaxFor(QuestionRole.SpamSignal, questions, result.Answers),
            MaxFor(QuestionRole.GenuineSignal, questions, result.Answers),
            stopwatch.ElapsedMilliseconds,
            result.InputTokens,
            result.OutputTokens));
    }

    // Mirrors DecisionRule: highest probability among core (guard.*) questions of the role.
    private static double MaxFor(QuestionRole role, IReadOnlyList<DecisionQuestion> questions, IReadOnlyList<DecisionAnswer> answers)
    {
        var keys = questions
            .Where(q => q.Role == role && q.Key.StartsWith(QuestionMerger.ReservedPrefix, StringComparison.Ordinal))
            .Select(q => q.Key)
            .ToHashSet(StringComparer.Ordinal);
        return answers.Where(a => keys.Contains(a.Key)).Select(a => a.Probability).DefaultIfEmpty(0.0).Max();
    }

    private static Guid ToGuid(object id) => id is Guid g ? g : Guid.Parse(id.ToString()!);

    public sealed record EvaluateRequest(string? Provider, string? Name, string? Email, string? Message);

    public sealed record EvaluateResponse(
        bool Success,
        string? Error,
        string? Band,
        double? SpamProbability,
        double? GenuineProbability,
        long ElapsedMs,
        long? InputTokens,
        long? OutputTokens);
}

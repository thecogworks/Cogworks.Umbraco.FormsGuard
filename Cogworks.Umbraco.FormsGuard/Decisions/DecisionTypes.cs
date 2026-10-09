namespace Cogworks.Umbraco.FormsGuard.Decisions;

/// <summary>How a question's answer feeds the decision rule.</summary>
public enum QuestionRole
{
    /// <summary>A high probability means the entry is likely spam.</summary>
    SpamSignal,

    /// <summary>A high probability means the entry is likely a genuine enquiry.</summary>
    GenuineSignal,

    /// <summary>Recorded only; does not affect the outcome.</summary>
    Informational,
}

/// <summary>The kind of answer a question expects.</summary>
public enum QuestionType
{
    /// <summary>Is this statement true? Answered with a probability from 0 to 1.</summary>
    Noul,

    /// <summary>Pick one of a set of options. Answered with the chosen key and per-option probabilities.</summary>
    Choice,
}

/// <summary>Lifecycle status of a decision for a Forms record.</summary>
public enum DecisionStatus
{
    Pending,
    Approved,
    ApprovedNotChecked,
    Quarantined,
    Review,
}

/// <summary>A question asked of the decision provider about one entry.</summary>
/// <param name="Key">Namespaced key, e.g. <c>guard.sales_pitch</c> or <c>triage.team</c>.</param>
/// <param name="Text">The question text.</param>
/// <param name="Role">How the answer feeds the decision rule.</param>
/// <param name="Type">The kind of answer expected.</param>
/// <param name="Options">Option key to option text; only for <see cref="QuestionType.Choice"/>.</param>
/// <param name="TrueCriteria">What makes a noul statement true; <c>null</c> omits it.</param>
/// <param name="FalseCriteria">What makes a noul statement false; <c>null</c> omits it.</param>
public sealed record DecisionQuestion(
    string Key,
    string Text,
    QuestionRole Role,
    QuestionType Type = QuestionType.Noul,
    IReadOnlyDictionary<string, string>? Options = null,
    string? TrueCriteria = null,
    string? FalseCriteria = null);

/// <summary>The allowed, trimmed view of an entry that is sent to the provider.</summary>
/// <param name="Organisation">Administrator-written description of the organisation.</param>
/// <param name="Form">The form's name.</param>
/// <param name="EmailDomain">Submitter email domain, only if the form allows it.</param>
/// <param name="Fields">Allowed field captions to trimmed values.</param>
public sealed record DecisionState(
    string Organisation,
    string Form,
    string? EmailDomain,
    IReadOnlyDictionary<string, string> Fields);

/// <summary>One request to a decision provider: all questions about one entry.</summary>
public sealed record DecisionRequest(
    Guid FormId,
    DecisionState State,
    IReadOnlyList<DecisionQuestion> Questions);

/// <summary>A provider's answer to one question.</summary>
/// <param name="Key">The question key.</param>
/// <param name="Probability">Probability the statement is true (noul) or confidence in the chosen option (choice).</param>
/// <param name="ChoiceKey">The chosen option key; only for choice questions.</param>
/// <param name="OptionProbabilities">Per-option probabilities; only for choice questions.</param>
public sealed record DecisionAnswer(
    string Key,
    double Probability,
    string? ChoiceKey = null,
    IReadOnlyDictionary<string, double>? OptionProbabilities = null);

/// <summary>The outcome of a provider call. Failures are values, never exceptions.</summary>
public sealed record DecisionResult
{
    private DecisionResult(
        bool success,
        IReadOnlyList<DecisionAnswer> answers,
        string? modelVersion,
        string? error,
        bool retryable,
        TimeSpan? retryAfter,
        long? inputTokens = null,
        long? outputTokens = null)
    {
        Success = success;
        Answers = answers;
        ModelVersion = modelVersion;
        Error = error;
        Retryable = retryable;
        RetryAfter = retryAfter;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
    }

    public bool Success { get; }

    public IReadOnlyList<DecisionAnswer> Answers { get; }

    public string? ModelVersion { get; }

    public string? Error { get; }

    /// <summary>True when the call may succeed if tried again (timeouts, rate limits, server errors).</summary>
    public bool Retryable { get; }

    /// <summary>How long the provider asked callers to wait before retrying, when it said.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Input tokens the provider reported for the call, when it said.</summary>
    public long? InputTokens { get; }

    /// <summary>Output tokens the provider reported for the call, when it said.</summary>
    public long? OutputTokens { get; }

    public static DecisionResult Succeeded(
        IReadOnlyList<DecisionAnswer> answers,
        string? modelVersion,
        long? inputTokens = null,
        long? outputTokens = null) =>
        new(true, answers, modelVersion, null, false, null, inputTokens, outputTokens);

    public static DecisionResult Failed(string error, bool retryable, TimeSpan? retryAfter = null) =>
        new(false, Array.Empty<DecisionAnswer>(), null, error, retryable, retryAfter);
}

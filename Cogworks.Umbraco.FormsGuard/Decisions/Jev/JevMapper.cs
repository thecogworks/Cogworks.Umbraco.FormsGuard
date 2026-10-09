using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cogworks.Umbraco.FormsGuard.Decisions.Jev;

/// <summary>
/// Pure mapping between Forms Guard decision types and the Jev <c>/v1/systemone</c> wire format.
/// No HTTP; every rule here is unit-testable.
/// </summary>
public static class JevMapper
{
    /// <summary>Builds the request body <c>{ state, model, questions }</c>.</summary>
    public static JsonObject BuildBody(DecisionRequest request, string model)
    {
        var fields = new JsonObject();
        foreach (var (caption, value) in request.State.Fields)
        {
            fields[caption] = value;
        }

        var state = new JsonObject
        {
            ["organisation"] = request.State.Organisation,
            ["form"] = request.State.Form,
            ["emailDomain"] = request.State.EmailDomain,
            ["fields"] = fields,
        };

        var questions = new JsonObject();
        foreach (var question in request.Questions)
        {
            questions[question.Key] = BuildQuestion(question);
        }

        return new JsonObject
        {
            ["state"] = state,
            ["model"] = model,
            ["questions"] = questions,
        };
    }

    private static JsonObject BuildQuestion(DecisionQuestion question)
    {
        var node = new JsonObject
        {
            ["type"] = question.Type == QuestionType.Choice ? "choice" : "noul",
            ["instructions"] = question.Text,
        };

        if (question.Type == QuestionType.Choice)
        {
            var options = new JsonObject();
            foreach (var (key, text) in question.Options ?? new Dictionary<string, string>())
            {
                options[key] = text;
            }

            node["criteria"] = options;
        }
        else if (!string.IsNullOrWhiteSpace(question.TrueCriteria) || !string.IsNullOrWhiteSpace(question.FalseCriteria))
        {
            var criteria = new JsonObject();
            if (!string.IsNullOrWhiteSpace(question.TrueCriteria))
            {
                criteria["true"] = question.TrueCriteria;
            }

            if (!string.IsNullOrWhiteSpace(question.FalseCriteria))
            {
                criteria["false"] = question.FalseCriteria;
            }

            node["criteria"] = criteria;
        }

        return node;
    }

    /// <summary>
    /// Parses a 200 response. Any malformed body, missing <c>answers</c>, missing requested key or
    /// non-numeric probability gives a retryable failure. Unrequested answers are ignored. Never throws.
    /// Optional top-level <c>usage.input_tokens</c> / <c>usage.output_tokens</c> are carried on the result;
    /// missing or malformed usage gives null counts, never a failure.
    /// </summary>
    public static DecisionResult Parse(string json, IReadOnlyList<DecisionQuestion> questions)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                return BadResponse("body is not a JSON object");
            }

            if (root["answers"] is not JsonObject answers)
            {
                return BadResponse("missing answers");
            }

            var model = root["model"] is JsonValue m && m.TryGetValue<string>(out var modelText) ? modelText : null;

            var result = new List<DecisionAnswer>(questions.Count);
            foreach (var question in questions)
            {
                if (answers[question.Key] is not JsonObject answer)
                {
                    return BadResponse("missing a requested answer");
                }

                var mapped = question.Type == QuestionType.Choice
                    ? ParseChoice(question.Key, answer)
                    : ParseNoul(question.Key, answer);
                if (mapped is null)
                {
                    return BadResponse("malformed answer");
                }

                result.Add(mapped);
            }

            return DecisionResult.Succeeded(
                result, model, TokenCount(root["usage"], "input_tokens"), TokenCount(root["usage"], "output_tokens"));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return BadResponse("invalid JSON");
        }
    }

    private static long? TokenCount(JsonNode? usage, string name)
    {
        if (usage is not JsonObject obj || obj[name] is not JsonValue value || value.GetValueKind() != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetValue<long>(out var count) && count >= 0 ? count : null;
    }

    private static DecisionAnswer? ParseNoul(string key, JsonObject answer) =>
        TryProbability(answer["noul"], out var p) ? new DecisionAnswer(key, p) : null;

    private static DecisionAnswer? ParseChoice(string key, JsonObject answer)
    {
        if (answer["choice"] is not JsonValue c || !c.TryGetValue<string>(out var choice))
        {
            return null;
        }

        if (answer["probabilities"] is not JsonObject probabilities)
        {
            return null;
        }

        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (option, value) in probabilities)
        {
            if (!TryProbability(value, out var p))
            {
                return null;
            }

            map[option] = p;
        }

        return map.TryGetValue(choice, out var chosen) ? new DecisionAnswer(key, chosen, choice, map) : null;
    }

    private static bool TryProbability(JsonNode? node, out double probability)
    {
        probability = 0;
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.Number)
        {
            return false;
        }

        probability = value.GetValue<double>();
        return probability is >= 0 and <= 1;
    }

    private static DecisionResult BadResponse(string reason) =>
        DecisionResult.Failed($"Jev returned an unusable response ({reason})", retryable: true);

    /// <summary>
    /// Turns a non-success status into a failure. 408, 429 and 5xx (including 529) are retryable.
    /// The error text carries only the status code, a short reason and the request id.
    /// </summary>
    public static DecisionResult Classify(
        HttpStatusCode status, RetryConditionHeaderValue? retryAfter, string? requestId, DateTimeOffset? now = null)
    {
        var code = (int)status;
        var retryable = code is 408 or 429 || code >= 500;
        var reason = code switch
        {
            400 => "bad request",
            401 => "unauthorised",
            403 => "forbidden",
            404 => "not found",
            408 => "request timeout",
            422 => "validation failed",
            429 => "rate limited",
            529 => "overloaded",
            >= 500 => "server error",
            _ => "unexpected status",
        };

        var error = $"Jev HTTP {code.ToString(CultureInfo.InvariantCulture)} ({reason})";
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            error += $"; request id {requestId}";
        }

        return DecisionResult.Failed(error, retryable, ToDelay(retryAfter, now ?? DateTimeOffset.UtcNow));
    }

    private static TimeSpan? ToDelay(RetryConditionHeaderValue? retryAfter, DateTimeOffset now)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - now;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }
}

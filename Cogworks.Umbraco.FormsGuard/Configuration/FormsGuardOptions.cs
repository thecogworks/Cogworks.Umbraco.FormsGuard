using System.ComponentModel.DataAnnotations;

namespace Cogworks.Umbraco.FormsGuard.Configuration;

public sealed class FormsGuardOptions
{
    public const string SectionName = "Cogworks:FormsGuard";

    public bool Enabled { get; set; } = true;

    /// <summary>How often the background decision processor runs, in seconds.</summary>
    public int ProcessorIntervalSeconds { get; set; } = 10;

    /// <summary>Maximum number of pending decisions claimed per processor run.</summary>
    public int ProcessorBatchSize { get; set; } = 10;

    /// <summary>Maximum number of claimed decisions processed at once. 1 to 32.</summary>
    public int ProcessorMaxConcurrency { get; set; } = 4;

    /// <summary>
    /// Age in seconds after which another server may take over a claim. At least 60. Must be longer than the
    /// longest time a single decision takes to process.
    /// </summary>
    public int ClaimStaleSeconds { get; set; } = 300;

    /// <summary>Delay in seconds before the first retry of a failed decision; doubles with each failure. At least 1.</summary>
    public int RetryBaseSeconds { get; set; } = 30;

    /// <summary>Longest retry delay in seconds, also the cap on a provider's Retry-After. At least <see cref="RetryBaseSeconds"/>.</summary>
    public int RetryMaxSeconds { get; set; } = 1800;

    /// <summary>Failed attempts after which the form's failure policy applies instead of another retry. At least 1.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>When on, no provider calls are made; entries not decided by a hard rule go straight to the form's failure policy.</summary>
    public bool KillSwitch { get; set; }

    /// <summary>
    /// Days a decision stays Quarantined before a daily job deletes its Forms record. 0 turns the purge off. Applies
    /// whatever <see cref="Enabled"/> and <see cref="KillSwitch"/> are set to. Other statuses follow the form's own
    /// Forms retention.
    /// </summary>
    public int QuarantineRetentionDays { get; set; } = 30;

    /// <summary>Verdict returned by the stub decision provider.</summary>
    public StubVerdict StubVerdict { get; set; } = StubVerdict.Approve;

    /// <summary>
    /// Alias of the decision provider to use, matched ignoring case: <c>jev</c> (default), <c>stub</c>, or
    /// <c>umbracoai</c> when the <c>Cogworks.Umbraco.FormsGuard.UmbracoAI</c> package is installed. An alias with
    /// no registered provider fails every decision without retries, so the form's failure policy applies.
    /// </summary>
    public string Provider { get; set; } = "jev";

    /// <summary>Settings for the Jev decision provider.</summary>
    public JevOptions Jev { get; set; } = new();
}

/// <summary>Settings for the Jev (TypeSafe AI) decision provider.</summary>
public sealed class JevOptions
{
    /// <summary>Environment variable read when <see cref="ApiKey"/> is blank.</summary>
    public const string ApiKeyEnvironmentVariable = "TYPESAFE_API_KEY";

    /// <summary>API key. Blank falls back to the <c>TYPESAFE_API_KEY</c> environment variable.</summary>
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.typesafe.ai";

    /// <summary>Pinned model version. Never <c>jev-latest</c>.</summary>
    public string Model { get; set; } = "jev-1.13.0";

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>The configured key, else the environment variable, else <c>null</c>.</summary>
    public string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            return ApiKey.Trim();
        }

        var fromEnv = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv.Trim();
    }
}

/// <summary>Fixed verdict for the stub decision provider.</summary>
public enum StubVerdict
{
    Approve,
    Reject,
}

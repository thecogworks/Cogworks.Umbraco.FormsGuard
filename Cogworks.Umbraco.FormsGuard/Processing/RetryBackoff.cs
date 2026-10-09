namespace Cogworks.Umbraco.FormsGuard.Processing;

/// <summary>Retry schedule for failed decisions.</summary>
public static class RetryBackoff
{
    /// <summary>
    /// The wait before the next attempt after the <paramref name="failures"/>th failure (counted from 1).
    /// A provider's <paramref name="retryAfter"/> wins, clamped to 1 s .. <paramref name="maxSeconds"/>;
    /// otherwise <c>min(baseSeconds × 2^(failures-1), maxSeconds)</c>.
    /// </summary>
    public static TimeSpan NextDelay(int failures, TimeSpan? retryAfter, int baseSeconds, int maxSeconds)
    {
        var max = Math.Max(1, maxSeconds);

        if (retryAfter is { } wait)
        {
            var seconds = Math.Clamp(wait.TotalSeconds, 1, max);
            return TimeSpan.FromSeconds(seconds);
        }

        var exponent = Math.Clamp(failures - 1, 0, 30);
        var delay = (long)Math.Max(1, baseSeconds) << exponent;
        return TimeSpan.FromSeconds(Math.Min(delay, max));
    }
}

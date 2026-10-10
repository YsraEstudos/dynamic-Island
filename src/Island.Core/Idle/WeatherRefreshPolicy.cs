namespace Island.Core.Idle;

/// <summary>
/// Timing rules of the weather refresh, as pure functions. Keep them here so tests can pin them down without a clock.
/// </summary>
public static class WeatherRefreshPolicy
{
    /// <summary>At most one network reading per interval, and only while the island is idle.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    /// <summary>After a failed reading, the next attempt waits this long instead of the full interval.</summary>
    public static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(5);

    /// <summary>A reading older than this is shown faded: the app has not managed to refresh it lately.</summary>
    public static readonly TimeSpan FadeAfter = TimeSpan.FromMinutes(45);

    /// <summary>A reading older than this is not shown at all (a forecast from yesterday is worse than nothing).</summary>
    public static readonly TimeSpan HideAfter = TimeSpan.FromHours(3);

    public static bool IsShown(DateTimeOffset fetchedAt, DateTimeOffset now) => now - fetchedAt <= HideAfter;

    public static bool IsFaded(DateTimeOffset fetchedAt, DateTimeOffset now) => now - fetchedAt > FadeAfter;

    /// <summary>
    /// How long to wait before the next attempt. With no reading and no failure the attempt is due at once. A failure
    /// newer than the last reading sets the backoff; otherwise the next attempt is one interval after the last reading.
    /// The result is clamped to [0, <see cref="Interval"/>], so a clock that jumped cannot park the timer for hours.
    /// </summary>
    public static TimeSpan DelayUntilNextAttempt(DateTimeOffset now, DateTimeOffset? lastSuccess, DateTimeOffset? lastFailure)
    {
        DateTimeOffset due;
        if (lastFailure is { } failed && (lastSuccess is null || failed >= lastSuccess.Value))
        {
            due = failed + FailureBackoff;
        }
        else if (lastSuccess is { } succeeded)
        {
            due = succeeded + Interval;
        }
        else
        {
            return TimeSpan.Zero;
        }

        TimeSpan delay = due - now;
        if (delay < TimeSpan.Zero) return TimeSpan.Zero;
        return delay > Interval ? Interval : delay;
    }
}

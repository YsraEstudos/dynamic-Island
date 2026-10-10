using Island.Core.Pomodoro;

namespace Island.Core.Idle;

/// <summary>Everything <see cref="IdleContentResolver"/> decides from. The resolver reads no clock, timer or service itself.</summary>
/// <param name="IdleModeEnabled">The Idle setting.</param>
/// <param name="IsCompact">The island is in its Compact mode (not Mini, Volume, Notice, the shelf or the clipboard).</param>
/// <param name="Suspended">The island is hidden (pause or full-screen app).</param>
/// <param name="PomodoroBusy">A pomodoro is running or paused with a plan in progress.</param>
/// <param name="MediaPlaying">Media is playing, so the equalizer owns the capsule.</param>
/// <param name="Scheduled">The pending scheduled pomodoro start, if any.</param>
/// <param name="Weather">The latest weather reading, if any.</param>
/// <param name="Now">The moment the decision is made, used for weather freshness.</param>
public sealed record IdleInputs(
    bool IdleModeEnabled,
    bool IsCompact,
    bool Suspended,
    bool PomodoroBusy,
    bool MediaPlaying,
    ScheduledStart? Scheduled,
    WeatherSnapshot? Weather,
    DateTimeOffset Now);

public static class IdleContentResolver
{
    /// <summary>
    /// The island is idle when the Compact capsule is visible with nothing else in it: the setting is on, the island is
    /// Compact and not suspended, no pomodoro is in progress and no media is playing. Only idle reads the weather.
    /// </summary>
    public static bool IsIdle(IdleInputs inputs) =>
        inputs.IdleModeEnabled && inputs.IsCompact && !inputs.Suspended && !inputs.PomodoroBusy && !inputs.MediaPlaying;

    /// <summary>
    /// Priority of the capsule, highest first: a running pomodoro (its countdown, shown elsewhere), media (the equalizer),
    /// a scheduled start, then the weather. The first two are not idle content, so they resolve to None and their owners
    /// keep the capsule. A weather reading older than <see cref="WeatherRefreshPolicy.HideAfter"/> is not shown.
    /// </summary>
    public static IdleContent Resolve(IdleInputs inputs)
    {
        if (!IsIdle(inputs)) return IdleContent.None;

        if (inputs.Scheduled is { } start) return new IdleContent.ScheduledPomodoro(start);

        if (inputs.Weather is { } weather && WeatherRefreshPolicy.IsShown(weather.FetchedAt, inputs.Now))
        {
            return new IdleContent.Weather(weather, WeatherRefreshPolicy.IsFaded(weather.FetchedAt, inputs.Now));
        }

        return IdleContent.None;
    }
}

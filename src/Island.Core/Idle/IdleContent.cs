using Island.Core.Pomodoro;

namespace Island.Core.Idle;

/// <summary>What the compact capsule shows while it is idle. Built by <see cref="IdleContentResolver.Resolve"/>.</summary>
public abstract record IdleContent
{
    /// <summary>Nothing to show: the capsule keeps its usual content.</summary>
    public static IdleContent None { get; } = new NoContent();

    public sealed record NoContent : IdleContent;

    /// <summary>
    /// Current weather. <paramref name="Faded"/> marks a reading the refresh has not renewed lately; it is still the
    /// best figure the app has.
    /// </summary>
    public sealed record Weather(WeatherSnapshot Snapshot, bool Faded) : IdleContent;

    /// <summary>The start time of a pomodoro that is scheduled but not running yet.</summary>
    public sealed record ScheduledPomodoro(ScheduledStart Start) : IdleContent;
}

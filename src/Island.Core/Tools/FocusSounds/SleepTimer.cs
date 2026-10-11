namespace Island.Core.Tools.FocusSounds;

/// <summary>
/// The sleep timer: a chosen duration (off, 15, 30 or 60 minutes) and the deadline of the run in progress. It reads the
/// clock from an injected <see cref="TimeProvider"/>, so tests can move time without waiting.
/// </summary>
public sealed class SleepTimer
{
    private static readonly int[] MinuteOptions = { 0, 15, 30, 60 };

    private readonly TimeProvider _time;
    private DateTimeOffset? _deadline;

    public SleepTimer(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>Selected duration in minutes, 0 when off.</summary>
    public int Minutes { get; private set; }

    public bool IsArmed => _deadline is not null;

    /// <summary>The next choice when the sleep button is pressed: off, 15, 30, 60, then off again.</summary>
    public static int NextOption(int minutes)
    {
        int index = Array.IndexOf(MinuteOptions, minutes);
        return MinuteOptions[(index + 1) % MinuteOptions.Length];
    }

    /// <summary>Selects a duration. Anything that is not one of the options is rejected.</summary>
    public void Select(int minutes)
    {
        if (Array.IndexOf(MinuteOptions, minutes) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Use 0, 15, 30 or 60 minutes.");
        }
        Minutes = minutes;
    }

    /// <summary>Starts a countdown from now. Does nothing while the timer is off.</summary>
    public void Arm()
    {
        _deadline = Minutes > 0 ? _time.GetUtcNow().AddMinutes(Minutes) : null;
    }

    public void Disarm() => _deadline = null;

    /// <summary>Time left in the running countdown, or null when none runs.</summary>
    public TimeSpan? Remaining()
    {
        if (_deadline is not { } deadline) return null;

        TimeSpan left = deadline - _time.GetUtcNow();
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }
}

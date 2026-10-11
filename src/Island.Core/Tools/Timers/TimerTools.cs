namespace Island.Core.Tools.Timers;

/// <summary>
/// The timer widget's long-lived state: both tools and the mode on screen. It lives in <see cref="Island.Core.Tools.ToolRegistry"/>,
/// so a running countdown keeps going when the shelf rebuilds the widget.
/// </summary>
public sealed class TimerTools : IDisposable
{
    public TimerTools(TimeProvider? clock = null, Action? alarm = null)
    {
        Stopwatch = new StopwatchModel(clock);
        Countdown = new CountdownModel(clock, alarm);
    }

    public StopwatchModel Stopwatch { get; }

    public CountdownModel Countdown { get; }

    public TimerMode Mode { get; private set; } = TimerMode.Countdown;

    public void SetMode(TimerMode mode) => Mode = mode;

    public void Dispose() => Countdown.Dispose();
}

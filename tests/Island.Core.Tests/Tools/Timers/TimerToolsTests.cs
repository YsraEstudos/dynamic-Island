using Island.Core.Tools.Timers;

namespace Island.Core.Tests.Tools.Timers;

public sealed class TimerToolsTests
{
    [Fact]
    public void Mode_starts_on_countdown_and_keeps_the_last_choice()
    {
        using var tools = new TimerTools(new ManualTimeProvider());
        Assert.Equal(TimerMode.Countdown, tools.Mode);

        tools.SetMode(TimerMode.Stopwatch);

        Assert.Equal(TimerMode.Stopwatch, tools.Mode);
    }

    [Fact]
    public void The_alarm_is_rung_by_the_countdown_when_it_ends()
    {
        var clock = new ManualTimeProvider();
        int alarms = 0;
        using var tools = new TimerTools(clock, () => alarms++);
        tools.Countdown.Adjust(TimeSpan.FromMinutes(-4));
        tools.Countdown.Start();

        clock.Advance(TimeSpan.FromMinutes(1));
        tools.Countdown.Tick();

        Assert.Equal(1, alarms);
    }
}

using Island.Core.Tools.FocusSounds;

namespace Island.Core.Tests.Tools.FocusSounds;

public class SleepTimerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 10, 20, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider _time = new(Start);

    [Fact]
    public void Off_never_counts_down()
    {
        var timer = new SleepTimer(_time);

        timer.Arm();

        Assert.False(timer.IsArmed);
        Assert.Null(timer.Remaining());
    }

    [Fact]
    public void The_countdown_follows_the_clock_and_stops_at_zero()
    {
        var timer = new SleepTimer(_time);
        timer.Select(15);
        timer.Arm();

        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(10), timer.Remaining());

        _time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(TimeSpan.Zero, timer.Remaining());
    }

    [Fact]
    public void Disarm_clears_a_running_countdown()
    {
        var timer = new SleepTimer(_time);
        timer.Select(30);
        timer.Arm();

        timer.Disarm();

        Assert.Null(timer.Remaining());
    }

    [Fact]
    public void The_button_cycles_off_15_30_60_and_back_to_off()
    {
        int minutes = 0;
        var visited = new List<int>();
        for (int step = 0; step < 4; step++)
        {
            minutes = SleepTimer.NextOption(minutes);
            visited.Add(minutes);
        }

        Assert.Equal(new[] { 15, 30, 60, 0 }, visited);
    }

    [Fact]
    public void An_unknown_duration_is_rejected()
    {
        var timer = new SleepTimer(_time);

        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Select(20));
    }
}

using Island.Core.Tools.Timers;

namespace Island.Core.Tests.Tools.Timers;

public sealed class StopwatchModelTests
{
    private readonly ManualTimeProvider _clock = new();

    [Fact]
    public void Elapsed_stays_zero_until_started()
    {
        var watch = new StopwatchModel(_clock);

        _clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.Zero, watch.Elapsed);
        Assert.False(watch.IsRunning);
    }

    [Fact]
    public void Pause_banks_the_time_and_later_runs_add_to_it()
    {
        var watch = new StopwatchModel(_clock);
        watch.Start();
        _clock.Advance(TimeSpan.FromSeconds(1.5));
        watch.Pause();
        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(TimeSpan.FromSeconds(1.5), watch.Elapsed);

        watch.Start();
        _clock.Advance(TimeSpan.FromSeconds(0.5));

        Assert.Equal(TimeSpan.FromSeconds(2), watch.Elapsed);
    }

    [Fact]
    public void Elapsed_is_read_from_the_clock_so_one_long_gap_is_counted_in_full()
    {
        var watch = new StopwatchModel(_clock);
        watch.Start();

        _clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(TimeSpan.FromHours(2), watch.Elapsed);
    }

    [Fact]
    public void Lap_records_lap_time_and_total_with_the_most_recent_first()
    {
        var watch = new StopwatchModel(_clock);
        watch.Start();
        _clock.Advance(TimeSpan.FromSeconds(2));
        watch.Lap();
        _clock.Advance(TimeSpan.FromSeconds(3));
        watch.Lap();

        IReadOnlyList<LapRecord> laps = watch.Laps;

        Assert.Equal(2, laps.Count);
        Assert.Equal(new LapRecord(2, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5)), laps[0]);
        Assert.Equal(new LapRecord(1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)), laps[1]);
    }

    [Fact]
    public void Lap_is_measured_from_the_previous_lap_and_skips_paused_time()
    {
        var watch = new StopwatchModel(_clock);
        watch.Start();
        _clock.Advance(TimeSpan.FromSeconds(2));
        watch.Pause();
        _clock.Advance(TimeSpan.FromMinutes(1));
        watch.Start();
        _clock.Advance(TimeSpan.FromSeconds(1));

        watch.Lap();

        Assert.Equal(new LapRecord(1, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3)), watch.Laps[0]);
    }

    [Fact]
    public void Lap_is_ignored_while_stopped()
    {
        var watch = new StopwatchModel(_clock);

        watch.Lap();
        watch.Start();
        _clock.Advance(TimeSpan.FromSeconds(1));
        watch.Pause();
        watch.Lap();

        Assert.Empty(watch.Laps);
    }

    [Fact]
    public void Best_and_worst_laps_are_the_fastest_and_the_slowest()
    {
        var watch = new StopwatchModel(_clock);
        watch.Start();
        _clock.Advance(TimeSpan.FromSeconds(3));
        watch.Lap();
        _clock.Advance(TimeSpan.FromSeconds(1));
        watch.Lap();
        _clock.Advance(TimeSpan.FromSeconds(2));
        watch.Lap();

        Assert.Equal(2, watch.BestLap!.Number);
        Assert.Equal(1, watch.WorstLap!.Number);
    }

    [Fact]
    public void Best_and_worst_are_absent_with_one_lap_or_when_all_laps_tie()
    {
        var watch = new StopwatchModel(_clock);
        watch.Start();
        _clock.Advance(TimeSpan.FromSeconds(2));
        watch.Lap();

        Assert.Null(watch.BestLap);
        Assert.Null(watch.WorstLap);

        _clock.Advance(TimeSpan.FromSeconds(2));
        watch.Lap();

        Assert.Null(watch.BestLap);
        Assert.Null(watch.WorstLap);
    }

    [Fact]
    public void Reset_stops_the_watch_and_clears_time_and_laps()
    {
        var watch = new StopwatchModel(_clock);
        watch.Start();
        _clock.Advance(TimeSpan.FromSeconds(4));
        watch.Lap();

        watch.Reset();
        _clock.Advance(TimeSpan.FromSeconds(4));

        Assert.False(watch.IsRunning);
        Assert.Equal(TimeSpan.Zero, watch.Elapsed);
        Assert.Empty(watch.Laps);
    }

    [Fact]
    public void Changed_fires_on_transitions_and_not_for_no_op_calls()
    {
        var watch = new StopwatchModel(_clock);
        int changes = 0;
        watch.Changed += () => changes++;

        watch.Start();
        _clock.Advance(TimeSpan.FromSeconds(1));
        watch.Lap();
        watch.Pause();
        watch.Pause();
        watch.Reset();
        watch.Reset();

        Assert.Equal(4, changes);
    }
}

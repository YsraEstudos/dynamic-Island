using Island.Core.Tools.Timers;

namespace Island.Core.Tests.Tools.Timers;

public sealed class CountdownModelTests
{
    private readonly ManualTimeProvider _clock = new();
    private int _alarms;
    private int _finishes;

    private CountdownModel Create()
    {
        var countdown = new CountdownModel(_clock, () => _alarms++);
        countdown.Finished += () => _finishes++;
        return countdown;
    }

    [Fact]
    public void Starts_idle_at_five_minutes()
    {
        using CountdownModel countdown = Create();

        Assert.Equal(CountdownState.Idle, countdown.State);
        Assert.Equal(TimeSpan.FromMinutes(5), countdown.Duration);
        Assert.Equal(TimeSpan.FromMinutes(5), countdown.Remaining);
        Assert.Equal(0, countdown.Progress);
    }

    [Fact]
    public void Remaining_and_progress_follow_the_clock()
    {
        using CountdownModel countdown = Create();
        countdown.Start();

        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(TimeSpan.FromMinutes(4), countdown.Remaining);
        Assert.Equal(0.2, countdown.Progress, 6);
    }

    [Fact]
    public void Pause_keeps_the_remaining_time_and_resume_continues_from_it()
    {
        using CountdownModel countdown = Create();
        countdown.Start();
        _clock.Advance(TimeSpan.FromSeconds(30));
        countdown.Pause();
        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(CountdownState.Paused, countdown.State);
        Assert.Equal(TimeSpan.FromMinutes(4.5), countdown.Remaining);

        countdown.Start();
        _clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromMinutes(4), countdown.Remaining);
    }

    [Fact]
    public void Finish_is_raised_once_when_the_time_runs_out()
    {
        using CountdownModel countdown = Create();
        countdown.Adjust(TimeSpan.FromMinutes(-4));
        countdown.Start();
        _clock.Advance(TimeSpan.FromSeconds(59));

        countdown.Tick();

        Assert.Equal(CountdownState.Running, countdown.State);
        Assert.Equal(0, _finishes);

        _clock.Advance(TimeSpan.FromSeconds(1));
        countdown.Tick();
        countdown.Tick();

        Assert.Equal(CountdownState.Finished, countdown.State);
        Assert.Equal(TimeSpan.Zero, countdown.Remaining);
        Assert.Equal(1, countdown.Progress);
        Assert.Equal(1, _finishes);
        Assert.Equal(1, _alarms);
    }

    [Fact]
    public void Pausing_before_the_end_prevents_the_finish()
    {
        using CountdownModel countdown = Create();
        countdown.Adjust(TimeSpan.FromMinutes(-4));
        countdown.Start();
        _clock.Advance(TimeSpan.FromSeconds(30));
        countdown.Pause();

        _clock.Advance(TimeSpan.FromMinutes(10));
        countdown.Tick();

        Assert.Equal(CountdownState.Paused, countdown.State);
        Assert.Equal(0, _finishes);
        Assert.Equal(0, _alarms);
    }

    [Fact]
    public void Reset_returns_to_the_full_duration_and_cancels_the_end()
    {
        using CountdownModel countdown = Create();
        countdown.Adjust(TimeSpan.FromMinutes(-4));
        countdown.Start();
        _clock.Advance(TimeSpan.FromSeconds(30));

        countdown.Reset();
        _clock.Advance(TimeSpan.FromMinutes(10));
        countdown.Tick();

        Assert.Equal(CountdownState.Idle, countdown.State);
        Assert.Equal(TimeSpan.FromMinutes(1), countdown.Remaining);
        Assert.Equal(0, _finishes);
    }

    [Fact]
    public void Starting_after_the_finish_runs_the_full_duration_again()
    {
        using CountdownModel countdown = Create();
        countdown.Adjust(TimeSpan.FromMinutes(-4));
        countdown.Start();
        _clock.Advance(TimeSpan.FromMinutes(1));
        countdown.Tick();

        countdown.Start();

        Assert.Equal(CountdownState.Running, countdown.State);
        Assert.Equal(TimeSpan.FromMinutes(1), countdown.Remaining);
    }

    [Fact]
    public void Preset_cycles_through_the_list_and_wraps_to_the_first()
    {
        using CountdownModel countdown = Create();
        int[] expected = { 10, 15, 25, 45, 60, 1, 3, 5 };

        foreach (int minutes in expected)
        {
            countdown.CyclePreset();
            Assert.Equal(TimeSpan.FromMinutes(minutes), countdown.Duration);
        }
    }

    [Fact]
    public void Presets_are_the_eight_durations_from_one_to_sixty_minutes()
    {
        Assert.Equal(
            new[] { 1, 3, 5, 10, 15, 25, 45, 60 }.Select(minutes => TimeSpan.FromMinutes(minutes)),
            CountdownModel.Presets);
    }

    [Fact]
    public void Preset_and_fine_adjust_are_ignored_while_running()
    {
        using CountdownModel countdown = Create();
        countdown.Start();

        countdown.CyclePreset();
        countdown.Adjust(TimeSpan.FromMinutes(1));

        Assert.Equal(TimeSpan.FromMinutes(5), countdown.Duration);
    }

    [Fact]
    public void Fine_adjust_steps_by_the_given_amount_and_stays_within_limits()
    {
        using CountdownModel countdown = Create();

        countdown.Adjust(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(6), countdown.Duration);

        countdown.Adjust(TimeSpan.FromSeconds(-10));
        Assert.Equal(TimeSpan.FromSeconds(350), countdown.Duration);

        countdown.Adjust(TimeSpan.FromHours(-1));
        Assert.Equal(CountdownModel.MinDuration, countdown.Duration);

        countdown.Adjust(TimeSpan.FromHours(5));
        Assert.Equal(CountdownModel.MaxDuration, countdown.Duration);
    }

    [Fact]
    public void Adjusting_a_paused_countdown_starts_over_at_the_new_duration()
    {
        using CountdownModel countdown = Create();
        countdown.Start();
        _clock.Advance(TimeSpan.FromMinutes(1));
        countdown.Pause();

        countdown.Adjust(TimeSpan.FromMinutes(1));

        Assert.Equal(CountdownState.Idle, countdown.State);
        Assert.Equal(TimeSpan.FromMinutes(6), countdown.Remaining);
    }

    [Fact]
    public void Changed_fires_on_transitions_but_not_while_nothing_moves()
    {
        using CountdownModel countdown = Create();
        int changes = 0;
        countdown.Changed += () => changes++;

        countdown.Start();
        _clock.Advance(TimeSpan.FromSeconds(10));
        countdown.Tick();
        countdown.Pause();
        countdown.Pause();

        Assert.Equal(2, changes);
    }

    [Fact]
    public void Dispose_cancels_a_pending_finish_and_can_be_repeated()
    {
        CountdownModel countdown = Create();
        countdown.Adjust(TimeSpan.FromMinutes(-4));
        countdown.Start();
        _clock.Advance(TimeSpan.FromMinutes(1));

        countdown.Dispose();
        countdown.Dispose();
        countdown.Tick();

        Assert.Equal(0, _finishes);
        Assert.Equal(0, _alarms);
    }
}

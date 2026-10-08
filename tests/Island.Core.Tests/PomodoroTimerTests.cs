using Island.Core.Abstractions;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Pomodoro;

namespace Island.Core.Tests;

public class PomodoroTimerTests
{
    private static readonly DateTimeOffset Start0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Clock that moves only when the test says so.</summary>
    private sealed class ManualClock
    {
        public DateTimeOffset Now { get; private set; } = Start0;

        public void Advance(TimeSpan by) => Now += by;

        public Func<DateTimeOffset> Read => () => Now;
    }

    private sealed class Rig : IDisposable
    {
        public ManualScheduler Scheduler { get; } = new();
        public ManualClock Clock { get; } = new();
        public PomodoroTimer Timer { get; }
        public List<PomodoroPhase> Completed { get; } = new();
        public int ChangedCount { get; private set; }

        /// <param name="settings">Read by the timer when it is constructed and on each phase change.</param>
        /// <param name="schedulerDrivesClock">When true the clock is the scheduler's virtual time, so callbacks see their exact due time.</param>
        public Rig(IslandSettings? settings = null, bool schedulerDrivesClock = true)
        {
            var current = settings ?? new IslandSettings();
            Func<DateTimeOffset> clock = schedulerDrivesClock
                ? () => Start0 + Scheduler.Now
                : Clock.Read;
            Timer = new PomodoroTimer(Scheduler, () => current, clock);
            Timer.PhaseCompleted += phase => Completed.Add(phase);
            Timer.Changed += () => ChangedCount++;
        }

        public void Advance(double seconds) => Scheduler.Advance(TimeSpan.FromSeconds(seconds));

        public void Dispose() => Timer.Dispose();
    }

    private static IslandSettings Minutes(int focus, int breakMinutes = 5) =>
        new() { PomodoroFocusMinutes = focus, PomodoroBreakMinutes = breakMinutes };

    [Fact]
    public void Starts_paused_in_focus_with_the_configured_duration()
    {
        using var rig = new Rig(Minutes(focus: 30));

        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(30), rig.Timer.PhaseDuration);
        Assert.Equal(TimeSpan.FromMinutes(30), rig.Timer.Remaining);
        Assert.Equal(0, rig.Scheduler.PendingCount);
    }

    [Fact]
    public void Start_counts_down_and_keeps_exactly_one_live_timer()
    {
        using var rig = new Rig();

        rig.Timer.Start();
        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(1, rig.Scheduler.PendingCount);

        rig.Advance(1);
        Assert.Equal(TimeSpan.FromMinutes(25) - TimeSpan.FromSeconds(1), rig.Timer.Remaining);
        Assert.Equal(1, rig.Scheduler.PendingCount);

        rig.Advance(30);
        Assert.Equal(TimeSpan.FromMinutes(25) - TimeSpan.FromSeconds(31), rig.Timer.Remaining);
        Assert.Equal(1, rig.Scheduler.PendingCount);
        Assert.Equal(31, rig.Scheduler.FiredCount);
    }

    [Fact]
    public void Ticks_land_on_whole_seconds_of_the_remaining_time()
    {
        using var rig = new Rig(Minutes(focus: 1));
        rig.Timer.Start();
        var changedAfterStart = rig.ChangedCount;

        // Remaining 60 s: no tick before the first full second.
        rig.Advance(0.4);
        Assert.Equal(changedAfterStart, rig.ChangedCount);
        Assert.Equal(TimeSpan.FromSeconds(60) - TimeSpan.FromSeconds(0.4), rig.Timer.Remaining);

        rig.Advance(0.6);
        Assert.Equal(changedAfterStart + 1, rig.ChangedCount);
        Assert.Equal(TimeSpan.FromSeconds(59), rig.Timer.Remaining);
    }

    [Fact]
    public void Pause_keeps_the_remaining_time_and_leaves_no_timer()
    {
        using var rig = new Rig();
        rig.Timer.Start();
        rig.Advance(10.5);

        rig.Timer.Pause();
        var remaining = rig.Timer.Remaining;
        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(0, rig.Scheduler.PendingCount);

        rig.Advance(100);
        Assert.Equal(remaining, rig.Timer.Remaining);
    }

    [Fact]
    public void Resume_continues_from_the_remaining_time_without_drift()
    {
        using var rig = new Rig(Minutes(focus: 1));
        rig.Timer.Start();
        rig.Advance(10.5);
        rig.Timer.Pause();
        Assert.Equal(TimeSpan.FromSeconds(60 - 10.5), rig.Timer.Remaining);

        rig.Timer.Start();
        Assert.Equal(1, rig.Scheduler.PendingCount);

        // Remaining 49.5 s: the next tick is 0.5 s away, landing on 49 s.
        rig.Advance(0.5);
        Assert.Equal(TimeSpan.FromSeconds(49), rig.Timer.Remaining);
        rig.Advance(20);
        Assert.Equal(TimeSpan.FromSeconds(29), rig.Timer.Remaining);
    }

    [Fact]
    public void Remaining_is_computed_from_the_clock_not_from_the_number_of_ticks()
    {
        using var rig = new Rig(Minutes(focus: 1), schedulerDrivesClock: false);
        rig.Timer.Start();

        // Wall time passes without the scheduler firing (a late timer): remaining follows the clock.
        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(50), rig.Timer.Remaining);

        // The tick fires and recomputes from the clock: 10 s of wall time, not one tick, has elapsed.
        rig.Advance(1);
        Assert.Equal(TimeSpan.FromSeconds(50), rig.Timer.Remaining);
        Assert.Equal(1, rig.Scheduler.PendingCount);
    }

    [Fact]
    public void Completing_focus_raises_once_and_switches_to_a_paused_break()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 2));
        rig.Timer.Start();

        rig.Advance(60);

        Assert.Equal(new[] { PomodoroPhase.Focus }, rig.Completed);
        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);
        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(2), rig.Timer.PhaseDuration);
        Assert.Equal(TimeSpan.FromMinutes(2), rig.Timer.Remaining);
        Assert.Equal(0, rig.Scheduler.PendingCount);

        // Nothing runs on its own after completion.
        rig.Advance(1000);
        Assert.Equal(new[] { PomodoroPhase.Focus }, rig.Completed);
        Assert.Equal(TimeSpan.FromMinutes(2), rig.Timer.Remaining);
    }

    [Fact]
    public void Completing_break_switches_back_to_focus()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 1));
        rig.Timer.SetPhase(PomodoroPhase.Break);
        rig.Timer.Start();

        rig.Advance(60);

        Assert.Equal(new[] { PomodoroPhase.Break }, rig.Completed);
        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.Equal(TimeSpan.FromMinutes(1), rig.Timer.Remaining);
    }

    [Fact]
    public void Start_while_running_is_a_no_op()
    {
        using var rig = new Rig();
        rig.Timer.Start();
        rig.Advance(5);
        var fired = rig.Scheduler.FiredCount;

        rig.Timer.Start();

        Assert.Equal(1, rig.Scheduler.PendingCount);
        Assert.Equal(fired, rig.Scheduler.FiredCount);
        Assert.Equal(TimeSpan.FromMinutes(25) - TimeSpan.FromSeconds(5), rig.Timer.Remaining);
    }

    [Fact]
    public void Toggle_alternates_between_running_and_paused()
    {
        using var rig = new Rig();

        rig.Timer.Toggle();
        Assert.True(rig.Timer.IsRunning);
        rig.Advance(3);

        rig.Timer.Toggle();
        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25) - TimeSpan.FromSeconds(3), rig.Timer.Remaining);
        Assert.Equal(0, rig.Scheduler.PendingCount);
    }

    [Fact]
    public void Reset_stops_and_restores_the_full_phase_duration()
    {
        using var rig = new Rig();
        rig.Timer.Start();
        rig.Advance(10);

        rig.Timer.Reset();

        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25), rig.Timer.Remaining);
        Assert.Equal(0, rig.Scheduler.PendingCount);
        Assert.Empty(rig.Completed);
    }

    [Fact]
    public void Reset_keeps_a_custom_duration_set_with_SetMinutes()
    {
        using var rig = new Rig();
        rig.Timer.SetMinutes(7);
        rig.Timer.Start();
        rig.Advance(1);

        rig.Timer.Reset();

        Assert.Equal(TimeSpan.FromMinutes(7), rig.Timer.PhaseDuration);
        Assert.Equal(TimeSpan.FromMinutes(7), rig.Timer.Remaining);
    }

    [Fact]
    public void SetPhase_stops_and_loads_that_phases_configured_duration()
    {
        using var rig = new Rig(Minutes(focus: 25, breakMinutes: 5));
        rig.Timer.Start();
        rig.Advance(2);

        rig.Timer.SetPhase(PomodoroPhase.Break);
        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);
        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(5), rig.Timer.Remaining);
        Assert.Equal(0, rig.Scheduler.PendingCount);

        rig.Timer.SetPhase(PomodoroPhase.Focus);
        Assert.Equal(TimeSpan.FromMinutes(25), rig.Timer.Remaining);
    }

    [Fact]
    public void SetMinutes_clamps_to_one_and_one_hundred_and_twenty()
    {
        using var rig = new Rig();

        rig.Timer.SetMinutes(0);
        Assert.Equal(TimeSpan.FromMinutes(1), rig.Timer.PhaseDuration);
        Assert.Equal(TimeSpan.FromMinutes(1), rig.Timer.Remaining);

        rig.Timer.SetMinutes(-5);
        Assert.Equal(TimeSpan.FromMinutes(1), rig.Timer.PhaseDuration);

        rig.Timer.SetMinutes(500);
        Assert.Equal(TimeSpan.FromMinutes(120), rig.Timer.PhaseDuration);
        Assert.Equal(TimeSpan.FromMinutes(120), rig.Timer.Remaining);

        rig.Timer.SetMinutes(45);
        Assert.Equal(TimeSpan.FromMinutes(45), rig.Timer.Remaining);
    }

    [Fact]
    public void SetMinutes_stops_a_running_timer_and_sets_the_current_phase_only()
    {
        using var rig = new Rig();
        rig.Timer.Start();
        rig.Advance(5);

        rig.Timer.SetMinutes(10);

        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(0, rig.Scheduler.PendingCount);
        Assert.Equal(TimeSpan.FromMinutes(10), rig.Timer.Remaining);
        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
    }

    [Fact]
    public void Changed_is_raised_on_ticks_and_on_control_calls()
    {
        using var rig = new Rig();

        rig.Timer.Start();
        Assert.Equal(1, rig.ChangedCount);

        rig.Advance(3);
        Assert.Equal(4, rig.ChangedCount);

        rig.Timer.Pause();
        Assert.Equal(5, rig.ChangedCount);

        rig.Timer.Reset();
        Assert.Equal(6, rig.ChangedCount);
    }

    [Fact]
    public void Dispose_cancels_the_live_timer_and_ignores_later_calls()
    {
        using var rig = new Rig();
        rig.Timer.Start();
        Assert.Equal(1, rig.Scheduler.PendingCount);

        rig.Timer.Dispose();
        Assert.Equal(0, rig.Scheduler.PendingCount);

        rig.Timer.Start();
        Assert.Equal(0, rig.Scheduler.PendingCount);
        Assert.False(rig.Timer.IsRunning);
    }

    [Fact]
    public void Stale_callback_from_a_paused_timer_does_nothing()
    {
        var scheduler = new KeepAllScheduler();
        var clock = new ManualClock();
        using var timer = new PomodoroTimer(scheduler, () => new IslandSettings(), clock.Read);

        timer.Start();
        var stale = scheduler.All[^1];
        timer.Pause();

        stale.Callback();

        Assert.False(timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.Remaining);
    }

    [Fact]
    public void Concurrent_control_calls_never_leave_more_than_one_live_timer()
    {
        using var rig = new Rig();

        Parallel.For(0, 200, i =>
        {
            switch (i % 4)
            {
                case 0: rig.Timer.Start(); break;
                case 1: rig.Timer.Pause(); break;
                case 2: rig.Timer.Toggle(); break;
                default: rig.Timer.SetMinutes(3); break;
            }
        });

        Assert.True(rig.Scheduler.PendingCount <= 1);
        Assert.Equal(rig.Timer.IsRunning, rig.Scheduler.PendingCount == 1);
    }

    /// <summary>Keeps every scheduled callback so a test can run a stale one directly.</summary>
    private sealed class KeepAllScheduler : IIslandScheduler
    {
        public sealed class Scheduled : IDisposable
        {
            public required Action Callback { get; init; }
            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;
        }

        public List<Scheduled> All { get; } = new();

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var scheduled = new Scheduled { Callback = callback };
            All.Add(scheduled);
            return scheduled;
        }
    }
}

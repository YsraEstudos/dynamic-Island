using Island.Core.Abstractions;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Pomodoro;

namespace Island.Core.Tests;

public class PomodoroScheduleTests
{
    private static readonly DateTimeOffset Start0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private const string Phrase = "Eu desisto do meu foco agora, e sei disso.";

    private sealed class Rig : IDisposable
    {
        public ManualScheduler Scheduler { get; } = new();
        public PomodoroTimer Timer { get; }
        public AngryPomodoro Angry { get; }
        public PomodoroSchedule Schedule { get; }

        /// <summary>Pending value seen by each Changed raise, in order.</summary>
        public List<ScheduledStart?> ChangedPending { get; } = new();

        public Rig(IslandSettings? settings = null, IPomodoroScheduleStore? store = null)
        {
            var current = settings ?? Minutes(focus: 25);
            Timer = new PomodoroTimer(Scheduler, () => current, () => Start0 + Scheduler.Now);
            Angry = new AngryPomodoro(Timer, () => Phrase);
            Schedule = new PomodoroSchedule(Timer, Angry, Scheduler, () => Now, store);
            Schedule.Changed += () => ChangedPending.Add(Schedule.Pending);
        }

        public DateTimeOffset Now => Start0 + Scheduler.Now;

        public void Advance(double seconds) => Scheduler.Advance(TimeSpan.FromSeconds(seconds));

        public void Dispose()
        {
            Schedule.Dispose();
            Angry.Dispose();
            Timer.Dispose();
        }
    }

    private static IslandSettings Minutes(int focus, int breakMinutes = 5) =>
        new() { PomodoroFocusMinutes = focus, PomodoroBreakMinutes = breakMinutes };

    [Fact]
    public void Focus_start_fires_at_the_scheduled_time_and_starts_a_plan()
    {
        using var rig = new Rig();
        rig.Schedule.Add(rig.Now + TimeSpan.FromSeconds(10), ScheduledStartMode.Focus, 3);

        rig.Advance(9);
        Assert.False(rig.Timer.IsRunning);
        Assert.NotNull(rig.Schedule.Pending);

        rig.Advance(1);
        Assert.True(rig.Timer.IsRunning);
        Assert.True(rig.Timer.PlanActive);
        Assert.Equal(3, rig.Timer.TotalCycles);
        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.False(rig.Timer.ControlsLocked);
        Assert.False(rig.Angry.IsSessionActive);
        Assert.Null(rig.Schedule.Pending);
    }

    [Fact]
    public void Angry_start_fires_and_locks_a_plan_of_the_chosen_size()
    {
        using var rig = new Rig();
        rig.Schedule.Add(rig.Now + TimeSpan.FromSeconds(5), ScheduledStartMode.Angry, 2);

        rig.Advance(5);

        Assert.True(rig.Angry.IsLocked);
        Assert.True(rig.Angry.IsSessionActive);
        Assert.True(rig.Timer.ControlsLocked);
        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(2, rig.Timer.TotalCycles);
        Assert.Null(rig.Schedule.Pending);
    }

    [Fact]
    public void Angry_start_during_a_break_switches_to_a_fresh_focus_first()
    {
        using var rig = new Rig(Minutes(focus: 25, breakMinutes: 5));
        rig.Timer.SetPhase(PomodoroPhase.Break);
        rig.Schedule.Add(rig.Now + TimeSpan.FromSeconds(1), ScheduledStartMode.Angry, 1);

        rig.Advance(1);

        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.True(rig.Angry.IsLocked);
        Assert.Equal(TimeSpan.FromMinutes(25), rig.Timer.PhaseDuration);
    }

    [Fact]
    public void Cancel_prevents_firing()
    {
        using var rig = new Rig();
        rig.Schedule.Add(rig.Now + TimeSpan.FromMinutes(1), ScheduledStartMode.Focus, 2);

        rig.Schedule.Cancel();
        rig.Advance(3600);

        Assert.Null(rig.Schedule.Pending);
        Assert.Empty(rig.Schedule.Starts);
        Assert.False(rig.Timer.IsRunning);
        Assert.False(rig.Timer.PlanActive);
        Assert.Equal(0, rig.Scheduler.PendingCount);
    }

    [Fact]
    public void Only_the_earliest_start_is_armed_and_the_later_one_waits_its_turn()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 2));
        rig.Schedule.Add(rig.Now + TimeSpan.FromSeconds(10), ScheduledStartMode.Angry, 4);
        rig.Schedule.Add(rig.Now + TimeSpan.FromSeconds(30), ScheduledStartMode.Focus, 2);

        Assert.Equal(1, rig.Scheduler.PendingCount);

        rig.Advance(10);
        Assert.True(rig.Angry.IsSessionActive);
        Assert.Equal(4, rig.Timer.TotalCycles);
        Assert.NotNull(rig.Schedule.Pending);

        // The Focus start is due while the Angry session owns the timer, so it is dropped.
        rig.Advance(20);
        Assert.True(rig.Angry.IsSessionActive);
        Assert.Equal(4, rig.Timer.TotalCycles);
        Assert.Empty(rig.Schedule.Starts);
    }

    [Fact]
    public void Past_time_fires_immediately()
    {
        using var rig = new Rig();

        var stored = rig.Schedule.Add(rig.Now.AddMinutes(-1), ScheduledStartMode.Focus, 2);

        Assert.Null(stored);
        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(2, rig.Timer.TotalCycles);
        Assert.Empty(rig.Schedule.Starts);
    }

    [Fact]
    public void Time_equal_to_now_fires_immediately()
    {
        using var rig = new Rig();

        Assert.Null(rig.Schedule.Add(rig.Now, ScheduledStartMode.Angry, 1));

        Assert.True(rig.Angry.IsLocked);
        Assert.Null(rig.Schedule.Pending);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(100, 24)]
    [InlineData(7, 7)]
    public void Cycles_are_clamped_to_the_timer_range(int requested, int expected)
    {
        using var rig = new Rig();
        var start = rig.Schedule.Add(rig.Now + TimeSpan.FromSeconds(1), ScheduledStartMode.Focus, requested);

        Assert.Equal(expected, start!.Cycles);
        Assert.Equal(expected, rig.Schedule.Pending!.Cycles);

        rig.Advance(1);

        Assert.Equal(expected, rig.Timer.TotalCycles);
    }

    [Fact]
    public void Scheduled_focus_start_does_not_override_an_active_angry_session()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 2));
        rig.Angry.Engage(3);
        rig.Schedule.Add(rig.Now + TimeSpan.FromSeconds(10), ScheduledStartMode.Focus, 2);

        rig.Advance(10);

        Assert.True(rig.Angry.IsSessionActive);
        Assert.True(rig.Angry.IsLocked);
        Assert.True(rig.Timer.ControlsLocked);
        Assert.Equal(3, rig.Timer.TotalCycles);
        Assert.Empty(rig.Schedule.Starts);
    }

    [Fact]
    public void Scheduled_angry_start_does_not_override_an_active_angry_session()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 2));
        rig.Angry.Engage(3);
        rig.Schedule.Add(rig.Now + TimeSpan.FromSeconds(10), ScheduledStartMode.Angry, 1);

        rig.Advance(10);

        Assert.Equal(3, rig.Timer.TotalCycles);
        Assert.True(rig.Angry.IsLocked);
    }

    [Fact]
    public void Pending_is_cleared_before_Changed_is_raised_on_firing()
    {
        using var rig = new Rig();
        rig.Schedule.Add(rig.Now + TimeSpan.FromSeconds(5), ScheduledStartMode.Focus, 1);
        Assert.Single(rig.ChangedPending);
        Assert.NotNull(rig.ChangedPending[0]);

        rig.Advance(5);

        Assert.Equal(2, rig.ChangedPending.Count);
        Assert.Null(rig.ChangedPending[1]);
        Assert.Null(rig.Schedule.Pending);
    }

    [Fact]
    public void Cancel_raises_Changed_only_when_something_was_pending()
    {
        using var rig = new Rig();

        rig.Schedule.Cancel();
        Assert.Empty(rig.ChangedPending);

        rig.Schedule.Add(rig.Now + TimeSpan.FromMinutes(1), ScheduledStartMode.Focus, 1);
        rig.Schedule.Cancel();

        Assert.Equal(2, rig.ChangedPending.Count);
        Assert.Null(rig.ChangedPending[1]);
    }

    [Fact]
    public void Dispose_cancels_the_pending_start_and_later_calls_do_nothing()
    {
        using var rig = new Rig();
        rig.Schedule.Add(rig.Now + TimeSpan.FromMinutes(1), ScheduledStartMode.Focus, 2);

        rig.Schedule.Dispose();
        rig.Advance(3600);

        Assert.Equal(0, rig.Scheduler.PendingCount);
        Assert.False(rig.Timer.IsRunning);

        Assert.Null(rig.Schedule.Add(rig.Now.AddHours(1), ScheduledStartMode.Focus, 2));
        Assert.Null(rig.Schedule.Pending);
        Assert.False(rig.Schedule.Remove(Guid.NewGuid()));
        Assert.Equal(0, rig.Scheduler.PendingCount);
    }

    [Fact]
    public void Starts_on_different_days_are_listed_earliest_first()
    {
        using var rig = new Rig();
        var later = rig.Schedule.Add(rig.Now.AddDays(2), ScheduledStartMode.Angry, 3)!;
        var sooner = rig.Schedule.Add(rig.Now.AddDays(1), ScheduledStartMode.Focus, 2)!;

        Assert.Equal(new[] { sooner, later }, rig.Schedule.Starts);
        Assert.Equal(sooner, rig.Schedule.Pending);
        Assert.Equal(1, rig.Scheduler.PendingCount);
    }

    [Fact]
    public void Remove_takes_out_only_that_start_and_arms_the_next_one()
    {
        using var rig = new Rig();
        var first = rig.Schedule.Add(rig.Now + TimeSpan.FromSeconds(10), ScheduledStartMode.Focus, 1);
        var second = rig.Schedule.Add(rig.Now + TimeSpan.FromHours(1), ScheduledStartMode.Focus, 2)!;

        Assert.True(rig.Schedule.Remove(first!.Id));
        Assert.False(rig.Schedule.Remove(first.Id));

        Assert.Equal(new[] { second }, rig.Schedule.Starts);
        Assert.Equal(second, rig.Schedule.Pending);
        Assert.Equal(1, rig.Scheduler.PendingCount);

        rig.Advance(10);
        Assert.False(rig.Timer.IsRunning);

        rig.Advance(3590);
        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(2, rig.Timer.TotalCycles);
        Assert.Empty(rig.Schedule.Starts);
    }

    [Fact]
    public void Firing_the_first_start_leaves_the_second_pending()
    {
        using var rig = new Rig();
        rig.Schedule.Add(rig.Now + TimeSpan.FromSeconds(10), ScheduledStartMode.Focus, 1);
        var tomorrow = rig.Schedule.Add(rig.Now.AddDays(1), ScheduledStartMode.Focus, 2)!;

        rig.Advance(10);

        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(new[] { tomorrow }, rig.Schedule.Starts);
        Assert.Equal(tomorrow, rig.Schedule.Pending);
    }

    [Fact]
    public void Starts_due_at_the_same_moment_fire_in_order()
    {
        using var rig = new Rig();
        rig.Schedule.Add(rig.Now + TimeSpan.FromSeconds(10), ScheduledStartMode.Focus, 1);
        rig.Schedule.Add(rig.Now + TimeSpan.FromSeconds(10), ScheduledStartMode.Focus, 4);

        rig.Advance(10);

        // The second one replaces the first Focus plan, so the last start in order wins.
        Assert.Equal(4, rig.Timer.TotalCycles);
        Assert.Empty(rig.Schedule.Starts);
    }

    [Fact]
    public void NextOccurrence_is_today_when_the_time_is_still_ahead()
    {
        var now = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

        var next = PomodoroSchedule.NextOccurrence(new TimeOnly(18, 30), now);

        Assert.Equal(new DateTimeOffset(2026, 1, 1, 18, 30, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextOccurrence_is_tomorrow_when_the_time_has_passed()
    {
        var now = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

        var next = PomodoroSchedule.NextOccurrence(new TimeOnly(8, 0), now);

        Assert.Equal(new DateTimeOffset(2026, 1, 2, 8, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextOccurrence_is_tomorrow_when_the_time_is_exactly_now()
    {
        var now = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

        var next = PomodoroSchedule.NextOccurrence(new TimeOnly(9, 0), now);

        Assert.Equal(new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void NextOccurrence_keeps_the_offset_of_now()
    {
        var now = new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.FromHours(-3));

        var next = PomodoroSchedule.NextOccurrence(new TimeOnly(7, 15), now);

        Assert.Equal(new DateTimeOffset(2026, 1, 2, 7, 15, 0, TimeSpan.FromHours(-3)), next);
    }

    [Fact]
    public void At_combines_a_date_and_time_with_the_offset_of_now()
    {
        var now = new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.FromHours(-3));

        var at = PomodoroSchedule.At(new DateOnly(2026, 3, 15), new TimeOnly(7, 15), now);

        Assert.Equal(new DateTimeOffset(2026, 3, 15, 7, 15, 0, TimeSpan.FromHours(-3)), at);
    }
}

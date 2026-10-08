using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Pomodoro;

namespace Island.Core.Tests;

public class PomodoroPlanControlTests
{
    private static readonly DateTimeOffset Start0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private sealed class Rig : IDisposable
    {
        public ManualScheduler Scheduler { get; } = new();
        public PomodoroTimer Timer { get; }
        public List<PomodoroTransition> Transitions { get; } = new();
        public int ChangedCount { get; private set; }

        public Rig(IslandSettings? settings = null)
        {
            var current = settings ?? new IslandSettings();
            Timer = new PomodoroTimer(Scheduler, () => current, () => Start0 + Scheduler.Now);
            Timer.Transitioned += transition => Transitions.Add(transition);
            Timer.Changed += () => ChangedCount++;
        }

        public void Advance(double seconds) => Scheduler.Advance(TimeSpan.FromSeconds(seconds));

        public void Dispose() => Timer.Dispose();
    }

    private static IslandSettings Minutes(int focus, int breakMinutes = 5) =>
        new() { PomodoroFocusMinutes = focus, PomodoroBreakMinutes = breakMinutes };

    [Fact]
    public void SetPlanTotal_without_a_plan_returns_false_and_raises_nothing()
    {
        using var rig = new Rig();
        var changed = rig.ChangedCount;

        Assert.False(rig.Timer.SetPlanTotal(3));

        Assert.False(rig.Timer.PlanActive);
        Assert.Equal(changed, rig.ChangedCount);
    }

    [Fact]
    public void SetPlanTotal_increases_a_running_plan_without_touching_the_countdown()
    {
        using var rig = new Rig(Minutes(focus: 2, breakMinutes: 1));
        rig.Timer.StartPlan(3);
        rig.Advance(30);
        var changed = rig.ChangedCount;

        Assert.True(rig.Timer.SetPlanTotal(5));

        Assert.Equal((1, 5), (rig.Timer.Cycle, rig.Timer.TotalCycles));
        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(90), rig.Timer.Remaining);
        Assert.True(rig.ChangedCount > changed);
    }

    [Fact]
    public void SetPlanTotal_decreases_a_running_plan()
    {
        using var rig = new Rig(Minutes(focus: 2, breakMinutes: 1));
        rig.Timer.StartPlan(5);

        Assert.True(rig.Timer.SetPlanTotal(2));

        Assert.Equal(2, rig.Timer.TotalCycles);
        Assert.True(rig.Timer.IsRunning);
    }

    [Fact]
    public void SetPlanTotal_to_the_same_value_returns_false_and_raises_nothing()
    {
        using var rig = new Rig();
        rig.Timer.StartPlan(3);
        var changed = rig.ChangedCount;

        Assert.False(rig.Timer.SetPlanTotal(3));

        Assert.Equal(changed, rig.ChangedCount);
    }

    [Fact]
    public void SetPlanTotal_clamps_to_the_current_pomodoro_and_to_MaxCycles()
    {
        using var rig = new Rig(Minutes(focus: 2, breakMinutes: 1));
        rig.Timer.StartPlan(5);
        rig.Advance(120 + 60);
        Assert.Equal(2, rig.Timer.Cycle);

        Assert.True(rig.Timer.SetPlanTotal(1));
        Assert.Equal(2, rig.Timer.TotalCycles);

        Assert.False(rig.Timer.SetPlanTotal(0));
        Assert.Equal(2, rig.Timer.TotalCycles);

        Assert.True(rig.Timer.SetPlanTotal(99));
        Assert.Equal(PomodoroTimer.MaxCycles, rig.Timer.TotalCycles);
        Assert.Equal(24, PomodoroTimer.MaxCycles);
    }

    [Fact]
    public void SetPlanTotal_works_while_controls_are_locked()
    {
        using var rig = new Rig(Minutes(focus: 2, breakMinutes: 1));
        rig.Timer.StartPlan(3);
        rig.Timer.ControlsLocked = true;

        Assert.True(rig.Timer.SetPlanTotal(4));

        Assert.Equal(4, rig.Timer.TotalCycles);
        Assert.True(rig.Timer.IsRunning);
        Assert.True(rig.Timer.ControlsLocked);
    }

    [Fact]
    public void Lowering_the_total_to_the_current_cycle_ends_the_plan_after_the_current_break()
    {
        using var rig = new Rig(Minutes(focus: 2, breakMinutes: 1));
        rig.Timer.StartPlan(3);
        rig.Advance(120);
        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);

        Assert.True(rig.Timer.SetPlanTotal(1));
        rig.Advance(60);

        Assert.False(rig.Timer.PlanActive);
        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.True(rig.Transitions[^1].PlanFinished);
        Assert.Equal(0, rig.Scheduler.PendingCount);
    }

    [Fact]
    public void Lowering_the_total_during_a_focus_makes_it_the_last_pomodoro()
    {
        using var rig = new Rig(Minutes(focus: 2, breakMinutes: 1));
        rig.Timer.StartPlan(3);
        rig.Advance(60 + 120 + 30);
        Assert.Equal((2, 3), (rig.Timer.Cycle, rig.Timer.TotalCycles));

        Assert.True(rig.Timer.SetPlanTotal(2));
        rig.Advance(90);
        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);
        Assert.True(rig.Timer.IsRunning);

        rig.Advance(60);
        Assert.False(rig.Timer.PlanActive);
        Assert.False(rig.Timer.IsRunning);
    }

    [Fact]
    public void TimeToFinish_on_a_fresh_focus_is_the_whole_plan_of_idle_cycles()
    {
        using var rig = new Rig(Minutes(focus: 25, breakMinutes: 5));

        Assert.Equal(TimeSpan.FromMinutes(90), rig.Timer.TimeToFinish(3));
    }

    [Fact]
    public void TimeToFinish_clamps_the_idle_cycles()
    {
        using var rig = new Rig(Minutes(focus: 25, breakMinutes: 5));

        Assert.Equal(TimeSpan.FromMinutes(30), rig.Timer.TimeToFinish(0));
        Assert.Equal(TimeSpan.FromMinutes(30 * PomodoroTimer.MaxCycles), rig.Timer.TimeToFinish(99));
    }

    [Fact]
    public void TimeToFinish_mid_plan_counts_the_rest_of_the_current_focus_and_later_pomodoros()
    {
        using var rig = new Rig(Minutes(focus: 25, breakMinutes: 5));
        rig.Timer.StartPlan(3);
        rig.Advance(5 * 60);

        // 20 min of focus left, its 5 min break, then two more pomodoros of 30 min.
        Assert.Equal(TimeSpan.FromMinutes(85), rig.Timer.TimeToFinish(7));
    }

    [Fact]
    public void TimeToFinish_in_a_plan_break_counts_the_break_and_later_pomodoros()
    {
        using var rig = new Rig(Minutes(focus: 25, breakMinutes: 5));
        rig.Timer.StartPlan(3);
        rig.Advance(25 * 60);
        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);

        Assert.Equal(TimeSpan.FromMinutes(5 + 2 * 30), rig.Timer.TimeToFinish(1));
    }

    [Fact]
    public void TimeToFinish_without_a_plan_on_a_fresh_break_is_just_the_break()
    {
        using var rig = new Rig(Minutes(focus: 25, breakMinutes: 5));
        rig.Timer.SetPhase(PomodoroPhase.Break);

        Assert.Equal(TimeSpan.FromMinutes(5), rig.Timer.TimeToFinish(3));
    }

    [Fact]
    public void TimeToFinish_for_a_paused_single_focus_is_its_remaining_time()
    {
        using var rig = new Rig(Minutes(focus: 25, breakMinutes: 5));
        rig.Timer.Start();
        rig.Advance(60);
        rig.Timer.Pause();

        Assert.Equal(TimeSpan.FromMinutes(24), rig.Timer.TimeToFinish(3));
    }
}

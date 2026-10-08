using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Pomodoro;

namespace Island.Core.Tests;

public class PomodoroPlanTests
{
    private static readonly DateTimeOffset Start0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private sealed class Rig : IDisposable
    {
        public ManualScheduler Scheduler { get; } = new();
        public PomodoroTimer Timer { get; }
        public List<PomodoroPhase> Completed { get; } = new();
        public List<PomodoroTransition> Transitions { get; } = new();
        public int ChangedCount { get; private set; }

        /// <param name="settings">Read by the timer when it is constructed and on each phase change.</param>
        public Rig(IslandSettings? settings = null)
        {
            var current = settings ?? new IslandSettings();
            Timer = new PomodoroTimer(Scheduler, () => current, () => Start0 + Scheduler.Now);
            Timer.PhaseCompleted += phase => Completed.Add(phase);
            Timer.Transitioned += transition => Transitions.Add(transition);
            Timer.Changed += () => ChangedCount++;
        }

        public void Advance(double seconds) => Scheduler.Advance(TimeSpan.FromSeconds(seconds));

        public void Dispose() => Timer.Dispose();
    }

    private static IslandSettings Minutes(int focus, int breakMinutes = 5) =>
        new() { PomodoroFocusMinutes = focus, PomodoroBreakMinutes = breakMinutes };

    [Fact]
    public void Plan_of_three_runs_focus_and_break_three_times_then_stops_on_a_fresh_focus()
    {
        using var rig = new Rig(Minutes(focus: 2, breakMinutes: 1));

        rig.Timer.StartPlan(3);
        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.True(rig.Timer.IsRunning);

        rig.Advance(120);
        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);
        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(1), rig.Timer.Remaining);

        rig.Advance(60);
        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.True(rig.Timer.IsRunning);

        rig.Advance(120);
        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);
        Assert.True(rig.Timer.IsRunning);

        rig.Advance(60);
        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.True(rig.Timer.IsRunning);

        rig.Advance(120);
        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);
        Assert.True(rig.Timer.IsRunning);

        rig.Advance(60);
        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.False(rig.Timer.IsRunning);
        Assert.False(rig.Timer.PlanActive);
        Assert.Equal(TimeSpan.FromMinutes(2), rig.Timer.Remaining);
        Assert.Equal(0, rig.Scheduler.PendingCount);

        Assert.Equal(
            new[] { PomodoroPhase.Focus, PomodoroPhase.Break, PomodoroPhase.Focus, PomodoroPhase.Break, PomodoroPhase.Focus, PomodoroPhase.Break },
            rig.Completed);

        // Nothing runs on its own after the plan ends.
        rig.Advance(1000);
        Assert.Equal(6, rig.Completed.Count);
    }

    [Fact]
    public void Cycle_and_TotalCycles_follow_the_plan_through_every_phase()
    {
        using var rig = new Rig(Minutes(focus: 2, breakMinutes: 1));

        rig.Timer.StartPlan(3);
        Assert.Equal((1, 3), (rig.Timer.Cycle, rig.Timer.TotalCycles));

        rig.Advance(120);
        Assert.Equal((1, 3), (rig.Timer.Cycle, rig.Timer.TotalCycles));

        rig.Advance(60);
        Assert.Equal((2, 3), (rig.Timer.Cycle, rig.Timer.TotalCycles));

        rig.Advance(120);
        Assert.Equal((2, 3), (rig.Timer.Cycle, rig.Timer.TotalCycles));

        rig.Advance(60);
        Assert.Equal((3, 3), (rig.Timer.Cycle, rig.Timer.TotalCycles));

        rig.Advance(120);
        Assert.Equal((3, 3), (rig.Timer.Cycle, rig.Timer.TotalCycles));

        rig.Advance(60);
        Assert.Equal((0, 0), (rig.Timer.Cycle, rig.Timer.TotalCycles));
    }

    [Fact]
    public void Transitioned_reports_each_boundary_including_the_last_one()
    {
        using var rig = new Rig(Minutes(focus: 2, breakMinutes: 1));

        rig.Timer.StartPlan(3);
        rig.Advance(120 + 60 + 120 + 60 + 120 + 60);

        Assert.Equal(new[]
        {
            new PomodoroTransition(PomodoroPhase.Focus, PomodoroPhase.Break, 1, 3, false, true, TimeSpan.FromMinutes(1)),
            new PomodoroTransition(PomodoroPhase.Break, PomodoroPhase.Focus, 1, 3, false, true, TimeSpan.FromMinutes(2)),
            new PomodoroTransition(PomodoroPhase.Focus, PomodoroPhase.Break, 2, 3, false, true, TimeSpan.FromMinutes(1)),
            new PomodoroTransition(PomodoroPhase.Break, PomodoroPhase.Focus, 2, 3, false, true, TimeSpan.FromMinutes(2)),
            new PomodoroTransition(PomodoroPhase.Focus, PomodoroPhase.Break, 3, 3, false, true, TimeSpan.FromMinutes(1)),
            new PomodoroTransition(PomodoroPhase.Break, PomodoroPhase.Focus, 3, 3, true, false, TimeSpan.FromMinutes(2)),
        }, rig.Transitions);
    }

    [Fact]
    public void Pause_in_the_middle_keeps_the_plan_and_Start_resumes_it()
    {
        using var rig = new Rig(Minutes(focus: 2, breakMinutes: 1));
        rig.Timer.StartPlan(2);
        rig.Advance(30);

        rig.Timer.Pause();

        Assert.False(rig.Timer.IsRunning);
        Assert.True(rig.Timer.PlanActive);
        Assert.Equal((1, 2), (rig.Timer.Cycle, rig.Timer.TotalCycles));
        Assert.Equal(TimeSpan.FromSeconds(90), rig.Timer.Remaining);
        Assert.Equal(0, rig.Scheduler.PendingCount);

        rig.Advance(1000);
        Assert.Equal(TimeSpan.FromSeconds(90), rig.Timer.Remaining);

        rig.Timer.Start();
        rig.Advance(90);

        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);
        Assert.True(rig.Timer.IsRunning);
        Assert.True(rig.Timer.PlanActive);
        Assert.Equal((1, 2), (rig.Timer.Cycle, rig.Timer.TotalCycles));
    }

    [Fact]
    public void Reset_during_a_plan_returns_to_a_fresh_focus_and_clears_the_plan()
    {
        using var rig = new Rig(Minutes(focus: 2, breakMinutes: 1));
        rig.Timer.StartPlan(3);
        rig.Advance(120);
        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);

        rig.Timer.Reset();

        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.False(rig.Timer.IsRunning);
        Assert.False(rig.Timer.PlanActive);
        Assert.Equal((0, 0), (rig.Timer.Cycle, rig.Timer.TotalCycles));
        Assert.Equal(TimeSpan.FromMinutes(2), rig.Timer.PhaseDuration);
        Assert.Equal(TimeSpan.FromMinutes(2), rig.Timer.Remaining);
        Assert.Equal(0, rig.Scheduler.PendingCount);

        rig.Advance(1000);
        Assert.Equal(new[] { PomodoroPhase.Focus }, rig.Completed);
    }

    [Fact]
    public void SetPhase_clears_the_plan()
    {
        using var rig = new Rig(Minutes(focus: 2, breakMinutes: 1));
        rig.Timer.StartPlan(3);
        rig.Advance(10);

        rig.Timer.SetPhase(PomodoroPhase.Break);

        Assert.False(rig.Timer.IsRunning);
        Assert.False(rig.Timer.PlanActive);
        Assert.Equal((0, 0), (rig.Timer.Cycle, rig.Timer.TotalCycles));
        Assert.Equal(0, rig.Scheduler.PendingCount);
    }

    [Fact]
    public void SetMinutes_clears_the_plan()
    {
        using var rig = new Rig(Minutes(focus: 2, breakMinutes: 1));
        rig.Timer.StartPlan(3);
        rig.Advance(10);

        rig.Timer.SetMinutes(10);

        Assert.False(rig.Timer.IsRunning);
        Assert.False(rig.Timer.PlanActive);
        Assert.Equal((0, 0), (rig.Timer.Cycle, rig.Timer.TotalCycles));
        Assert.Equal(TimeSpan.FromMinutes(10), rig.Timer.Remaining);
        Assert.Equal(0, rig.Scheduler.PendingCount);
    }

    [Fact]
    public void StartPlan_is_ignored_while_controls_are_locked()
    {
        using var rig = new Rig();
        rig.Timer.ControlsLocked = true;
        var changed = rig.ChangedCount;

        rig.Timer.StartPlan(3);

        Assert.False(rig.Timer.IsRunning);
        Assert.False(rig.Timer.PlanActive);
        Assert.Equal(0, rig.Scheduler.PendingCount);
        Assert.Equal(changed, rig.ChangedCount);
    }

    [Fact]
    public void StartPlan_clamps_the_number_of_cycles()
    {
        using var rig = new Rig();

        rig.Timer.StartPlan(0);
        Assert.Equal((1, 1), (rig.Timer.Cycle, rig.Timer.TotalCycles));

        rig.Timer.StartPlan(99);
        Assert.Equal((1, 24), (rig.Timer.Cycle, rig.Timer.TotalCycles));
    }

    [Fact]
    public void Play_on_a_fresh_focus_starts_a_plan()
    {
        using var rig = new Rig();

        rig.Timer.Play(3);

        Assert.True(rig.Timer.IsRunning);
        Assert.True(rig.Timer.PlanActive);
        Assert.Equal((1, 3), (rig.Timer.Cycle, rig.Timer.TotalCycles));
    }

    [Fact]
    public void Play_on_a_half_done_focus_resumes_it_without_creating_a_plan()
    {
        using var rig = new Rig(Minutes(focus: 2));
        rig.Timer.Start();
        rig.Advance(30);
        rig.Timer.Pause();

        rig.Timer.Play(3);

        Assert.True(rig.Timer.IsRunning);
        Assert.False(rig.Timer.PlanActive);
        Assert.Equal((0, 0), (rig.Timer.Cycle, rig.Timer.TotalCycles));
        Assert.Equal(TimeSpan.FromSeconds(90), rig.Timer.Remaining);
    }

    [Fact]
    public void Play_on_a_fresh_break_starts_it_alone_without_a_plan()
    {
        using var rig = new Rig();
        rig.Timer.SetPhase(PomodoroPhase.Break);

        rig.Timer.Play(3);

        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);
        Assert.False(rig.Timer.PlanActive);
    }

    [Fact]
    public void Play_while_running_pauses_and_keeps_the_plan()
    {
        using var rig = new Rig();
        rig.Timer.Play(3);
        rig.Advance(5);

        rig.Timer.Play(3);

        Assert.False(rig.Timer.IsRunning);
        Assert.True(rig.Timer.PlanActive);
        Assert.Equal((1, 3), (rig.Timer.Cycle, rig.Timer.TotalCycles));
    }

    [Fact]
    public void Without_a_plan_a_natural_end_still_stops_on_the_next_phase()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 2));
        rig.Timer.Start();

        rig.Advance(60);

        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);
        Assert.False(rig.Timer.IsRunning);
        Assert.False(rig.Timer.PlanActive);
        Assert.Equal(0, rig.Scheduler.PendingCount);
        Assert.Equal(new[] { new PomodoroTransition(PomodoroPhase.Focus, PomodoroPhase.Break, 0, 0, false, false, TimeSpan.FromMinutes(2)) }, rig.Transitions);

        rig.Advance(1000);
        Assert.Single(rig.Transitions);
    }
}

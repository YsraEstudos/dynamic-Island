using Island.Core.Abstractions;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Pomodoro;

namespace Island.Core.Tests;

public class PomodoroTimerLockTests
{
    private static readonly DateTimeOffset Start0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private sealed class Rig : IDisposable
    {
        public ManualScheduler Scheduler { get; } = new();
        public PomodoroTimer Timer { get; }
        public List<PomodoroPhase> Completed { get; } = new();
        public int ChangedCount { get; private set; }

        public Rig(IslandSettings? settings = null)
        {
            var current = settings ?? new IslandSettings();
            Timer = new PomodoroTimer(Scheduler, () => current, () => Start0 + Scheduler.Now);
            Timer.PhaseCompleted += phase => Completed.Add(phase);
            Timer.Changed += () => ChangedCount++;
        }

        public void Advance(double seconds) => Scheduler.Advance(TimeSpan.FromSeconds(seconds));

        public void Dispose() => Timer.Dispose();
    }

    private static IslandSettings Minutes(int focus, int breakMinutes = 5) =>
        new() { PomodoroFocusMinutes = focus, PomodoroBreakMinutes = breakMinutes };

    [Fact]
    public void Controls_are_unlocked_by_default()
    {
        using var rig = new Rig();

        Assert.False(rig.Timer.ControlsLocked);
    }

    [Fact]
    public void Locked_Pause_is_a_silent_no_op()
    {
        using var rig = new Rig();
        rig.Timer.Start();
        rig.Advance(10);
        rig.Timer.ControlsLocked = true;
        var remaining = rig.Timer.Remaining;
        var changed = rig.ChangedCount;

        rig.Timer.Pause();

        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(remaining, rig.Timer.Remaining);
        Assert.Equal(1, rig.Scheduler.PendingCount);
        Assert.Equal(changed, rig.ChangedCount);
    }

    [Fact]
    public void Locked_Toggle_cannot_pause_a_running_timer()
    {
        using var rig = new Rig();
        rig.Timer.Start();
        rig.Timer.ControlsLocked = true;

        rig.Timer.Toggle();

        Assert.True(rig.Timer.IsRunning);
    }

    [Fact]
    public void Locked_Reset_is_a_silent_no_op()
    {
        using var rig = new Rig();
        rig.Timer.Start();
        rig.Advance(10);
        rig.Timer.ControlsLocked = true;
        var changed = rig.ChangedCount;

        rig.Timer.Reset();

        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25) - TimeSpan.FromSeconds(10), rig.Timer.Remaining);
        Assert.Equal(changed, rig.ChangedCount);
    }

    [Fact]
    public void Locked_SetPhase_is_a_silent_no_op()
    {
        using var rig = new Rig(Minutes(focus: 25, breakMinutes: 5));
        rig.Timer.ControlsLocked = true;
        var changed = rig.ChangedCount;

        rig.Timer.SetPhase(PomodoroPhase.Break);

        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.Equal(TimeSpan.FromMinutes(25), rig.Timer.PhaseDuration);
        Assert.Equal(changed, rig.ChangedCount);
    }

    [Fact]
    public void Locked_SetMinutes_is_a_silent_no_op()
    {
        using var rig = new Rig();
        rig.Timer.ControlsLocked = true;
        var changed = rig.ChangedCount;

        rig.Timer.SetMinutes(3);

        Assert.Equal(TimeSpan.FromMinutes(25), rig.Timer.PhaseDuration);
        Assert.Equal(TimeSpan.FromMinutes(25), rig.Timer.Remaining);
        Assert.Equal(changed, rig.ChangedCount);
    }

    [Fact]
    public void Locked_timer_still_starts()
    {
        using var rig = new Rig();
        rig.Timer.ControlsLocked = true;
        var changed = rig.ChangedCount;

        rig.Timer.Start();

        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(1, rig.Scheduler.PendingCount);
        Assert.Equal(changed + 1, rig.ChangedCount);
    }

    [Fact]
    public void Natural_completion_still_switches_to_break_while_locked()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 2));
        rig.Timer.ControlsLocked = true;
        rig.Timer.Start();

        rig.Advance(60);

        Assert.Equal(new[] { PomodoroPhase.Focus }, rig.Completed);
        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);
        Assert.False(rig.Timer.IsRunning);
        Assert.True(rig.Timer.ControlsLocked);
    }

    [Fact]
    public void Unlocking_restores_every_control()
    {
        using var rig = new Rig();
        rig.Timer.ControlsLocked = true;
        rig.Timer.Start();
        rig.Advance(5);

        rig.Timer.ControlsLocked = false;
        rig.Timer.Pause();
        Assert.False(rig.Timer.IsRunning);

        rig.Timer.SetMinutes(7);
        Assert.Equal(TimeSpan.FromMinutes(7), rig.Timer.Remaining);

        rig.Timer.Start();
        rig.Timer.Reset();
        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(7), rig.Timer.Remaining);
    }
}

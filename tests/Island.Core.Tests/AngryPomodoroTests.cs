using Island.Core.Abstractions;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Pomodoro;

namespace Island.Core.Tests;

public class AngryPomodoroTests
{
    private static readonly DateTimeOffset Start0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private const string Phrase = "Eu desisto do meu foco agora, e sei disso.";

    private sealed class Rig : IDisposable
    {
        public ManualScheduler Scheduler { get; } = new();
        public PomodoroTimer Timer { get; }
        public AngryPomodoro Angry { get; }

        /// <summary>(IsLocked, IsSessionActive) as each LockChanged saw it, in order.</summary>
        public List<(bool Locked, bool Session)> LockEvents { get; } = new();

        public Rig(IslandSettings? settings = null)
        {
            var current = settings ?? Minutes(focus: 25);
            Timer = new PomodoroTimer(Scheduler, () => current, () => Start0 + Scheduler.Now);
            Angry = new AngryPomodoro(Timer, () => Phrase);
            Angry.LockChanged += () => LockEvents.Add((Angry.IsLocked, Angry.IsSessionActive));
        }

        public void Advance(double seconds) => Scheduler.Advance(TimeSpan.FromSeconds(seconds));

        public void Dispose()
        {
            Angry.Dispose();
            Timer.Dispose();
        }
    }

    private static IslandSettings Minutes(int focus, int breakMinutes = 5) =>
        new() { PomodoroFocusMinutes = focus, PomodoroBreakMinutes = breakMinutes };

    [Fact]
    public void Engage_from_paused_focus_starts_and_locks_the_timer()
    {
        using var rig = new Rig();

        rig.Angry.Engage(1);

        Assert.True(rig.Angry.IsLocked);
        Assert.True(rig.Angry.IsSessionActive);
        Assert.True(rig.Timer.IsRunning);
        Assert.True(rig.Timer.ControlsLocked);
        Assert.Equal(new[] { (true, true) }, rig.LockEvents);
    }

    [Fact]
    public void Engage_from_break_switches_to_focus_first()
    {
        using var rig = new Rig(Minutes(focus: 25, breakMinutes: 5));
        rig.Timer.SetPhase(PomodoroPhase.Break);

        rig.Angry.Engage(1);

        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25), rig.Timer.Remaining);
    }

    [Fact]
    public void Engage_mid_run_locks_without_resetting_the_remaining_time()
    {
        using var rig = new Rig();
        rig.Timer.Start();
        rig.Advance(90);

        rig.Angry.Engage(3);

        Assert.True(rig.Angry.IsLocked);
        Assert.True(rig.Timer.IsRunning);
        Assert.False(rig.Timer.PlanActive);
        Assert.Equal(TimeSpan.FromMinutes(25) - TimeSpan.FromSeconds(90), rig.Timer.Remaining);
    }

    [Fact]
    public void Engage_with_three_starts_a_three_pomodoro_plan_and_locks()
    {
        using var rig = new Rig(Minutes(focus: 25, breakMinutes: 5));

        rig.Angry.Engage(3);

        Assert.True(rig.Timer.PlanActive);
        Assert.Equal(3, rig.Timer.TotalCycles);
        Assert.Equal(1, rig.Timer.Cycle);
        Assert.True(rig.Angry.IsLocked);
        Assert.True(rig.Timer.ControlsLocked);
        Assert.Equal(new[] { (true, true) }, rig.LockEvents);
    }

    [Fact]
    public void Focus_end_starts_a_free_break_and_keeps_the_session()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 2));
        rig.Angry.Engage(3);

        rig.Advance(60);

        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);
        Assert.True(rig.Timer.IsRunning);
        Assert.False(rig.Angry.IsLocked);
        Assert.True(rig.Angry.IsSessionActive);
        Assert.False(rig.Timer.ControlsLocked);
        Assert.Equal(new[] { (true, true), (false, true) }, rig.LockEvents);
    }

    [Fact]
    public void Break_end_locks_the_next_focus_again()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 2));
        rig.Angry.Engage(3);
        rig.Advance(60 + 120);

        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.True(rig.Timer.IsRunning);
        Assert.True(rig.Angry.IsLocked);
        Assert.True(rig.Angry.IsSessionActive);
        Assert.True(rig.Timer.ControlsLocked);
        Assert.Equal(2, rig.Timer.Cycle);
        Assert.Equal(new[] { (true, true), (false, true), (true, true) }, rig.LockEvents);
    }

    [Fact]
    public void Locked_focus_of_a_plan_cannot_be_paused_or_reset()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 2));
        rig.Angry.Engage(3);
        rig.Advance(60 + 120);

        rig.Timer.Pause();
        rig.Timer.Reset();

        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.True(rig.Angry.IsLocked);
    }

    [Fact]
    public void Last_break_ends_the_session_and_leaves_controls_free()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 2));
        rig.Angry.Engage(2);

        rig.Advance(60 + 120 + 60 + 120);

        Assert.False(rig.Angry.IsSessionActive);
        Assert.False(rig.Angry.IsLocked);
        Assert.Null(rig.Angry.CurrentPhrase);
        Assert.False(rig.Timer.ControlsLocked);
        Assert.False(rig.Timer.PlanActive);
        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(new[] { (true, true), (false, true), (true, true), (false, true), (false, false) }, rig.LockEvents);
    }

    [Fact]
    public void Natural_end_of_a_single_focus_ends_the_session()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 5));
        rig.Timer.Start();
        rig.Angry.Engage(3);

        rig.Advance(60);

        Assert.False(rig.Angry.IsLocked);
        Assert.False(rig.Angry.IsSessionActive);
        Assert.Null(rig.Angry.CurrentPhrase);
        Assert.False(rig.Timer.ControlsLocked);
        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);        Assert.Equal(new[] { (true, true), (false, false) }, rig.LockEvents);
    }

    [Fact]
    public void Reset_during_a_free_break_ends_the_session()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 2));
        rig.Angry.Engage(3);
        rig.Advance(60);
        Assert.True(rig.Angry.IsSessionActive);

        rig.Timer.Reset();

        Assert.False(rig.Angry.IsSessionActive);
        Assert.False(rig.Angry.IsLocked);
        Assert.False(rig.Timer.PlanActive);
        Assert.Equal(new[] { (true, true), (false, true), (false, false) }, rig.LockEvents);
    }

    [Fact]
    public void TryUnlock_during_a_break_ends_the_session_and_resets_the_timer()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 2));
        rig.Angry.Engage(3);
        rig.Advance(60);
        rig.Angry.NewPhrase();

        Assert.True(rig.Angry.TryUnlock(Phrase));

        Assert.False(rig.Angry.IsSessionActive);
        Assert.False(rig.Angry.IsLocked);
        Assert.False(rig.Timer.PlanActive);
        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.Equal(TimeSpan.FromMinutes(1), rig.Timer.Remaining);
        Assert.Equal(new[] { (true, true), (false, true), (false, false) }, rig.LockEvents);
    }

    [Fact]
    public void Engage_while_a_plan_is_in_its_break_keeps_the_session_free_and_locks_at_the_next_focus()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 2));
        rig.Timer.StartPlan(2);
        rig.Advance(60);
        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);

        rig.Angry.Engage(5);

        Assert.True(rig.Angry.IsSessionActive);
        Assert.False(rig.Angry.IsLocked);
        Assert.False(rig.Timer.ControlsLocked);
        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(2, rig.Timer.TotalCycles);

        rig.Advance(120);

        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.True(rig.Timer.IsRunning);
        Assert.True(rig.Angry.IsLocked);
        Assert.True(rig.Timer.ControlsLocked);
        Assert.Equal(new[] { (false, true), (true, true) }, rig.LockEvents);
    }

    [Fact]
    public void Second_Engage_is_a_no_op()
    {
        using var rig = new Rig();
        rig.Angry.Engage(3);
        var remaining = rig.Timer.Remaining;

        rig.Angry.Engage(3);

        Assert.Equal(new[] { (true, true) }, rig.LockEvents);
        Assert.Equal(1, rig.Scheduler.PendingCount);
        Assert.Equal(remaining, rig.Timer.Remaining);
    }

    [Fact]
    public void Locked_timer_cannot_be_paused_or_reset()
    {
        using var rig = new Rig();
        rig.Angry.Engage(1);
        rig.Advance(5);

        rig.Timer.Pause();
        rig.Timer.Reset();

        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25) - TimeSpan.FromSeconds(5), rig.Timer.Remaining);
        Assert.True(rig.Angry.IsLocked);
    }

    [Fact]
    public void Wrong_phrase_keeps_the_lock()
    {
        using var rig = new Rig();
        rig.Angry.Engage(1);
        rig.Angry.NewPhrase();

        Assert.False(rig.Angry.TryUnlock("Eu desisto do meu foco agora, e sei disso"));
        Assert.False(rig.Angry.TryUnlock("eu desisto do meu foco agora, e sei disso."));
        Assert.False(rig.Angry.TryUnlock(null));

        Assert.True(rig.Angry.IsLocked);
        Assert.True(rig.Timer.ControlsLocked);
        Assert.Equal(new[] { (true, true) }, rig.LockEvents);
    }

    [Fact]
    public void Right_phrase_unlocks_and_resets_the_timer_to_full_duration()
    {
        using var rig = new Rig();
        rig.Angry.Engage(1);
        rig.Advance(30);
        rig.Angry.NewPhrase();

        Assert.True(rig.Angry.TryUnlock(Phrase));

        Assert.False(rig.Angry.IsLocked);
        Assert.False(rig.Angry.IsSessionActive);
        Assert.False(rig.Timer.ControlsLocked);
        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25), rig.Timer.Remaining);
        Assert.Equal(new[] { (true, true), (false, false) }, rig.LockEvents);
    }

    [Fact]
    public void Right_phrase_during_a_focus_of_a_plan_clears_the_plan()
    {
        using var rig = new Rig();
        rig.Angry.Engage(3);
        rig.Angry.NewPhrase();

        Assert.True(rig.Angry.TryUnlock(Phrase));

        Assert.False(rig.Angry.IsSessionActive);
        Assert.False(rig.Timer.PlanActive);
        Assert.False(rig.Timer.ControlsLocked);
        Assert.Equal(TimeSpan.FromMinutes(25), rig.Timer.Remaining);
    }

    [Fact]
    public void Unlocked_timer_accepts_pause_and_reset_again()
    {
        using var rig = new Rig();
        rig.Angry.Engage(1);
        rig.Angry.NewPhrase();
        rig.Angry.TryUnlock(Phrase);

        rig.Timer.Start();
        rig.Advance(4);
        rig.Timer.Pause();
        Assert.False(rig.Timer.IsRunning);

        rig.Timer.Reset();
        Assert.Equal(TimeSpan.FromMinutes(25), rig.Timer.Remaining);
    }

    [Fact]
    public void TryUnlock_when_no_session_is_true()
    {
        using var rig = new Rig();

        Assert.True(rig.Angry.TryUnlock("qualquer coisa"));
        Assert.False(rig.Angry.IsLocked);
        Assert.Empty(rig.LockEvents);
    }

    [Fact]
    public void Dispose_clears_ControlsLocked_and_unlocks()
    {
        using var rig = new Rig();
        rig.Angry.Engage(1);

        rig.Angry.Dispose();

        Assert.False(rig.Angry.IsLocked);
        Assert.False(rig.Angry.IsSessionActive);
        Assert.False(rig.Timer.ControlsLocked);
        rig.Timer.Pause();
        Assert.False(rig.Timer.IsRunning);
    }

    [Fact]
    public void Engage_after_Dispose_is_a_no_op()
    {
        using var rig = new Rig();
        rig.Angry.Dispose();

        rig.Angry.Engage(3);

        Assert.False(rig.Angry.IsLocked);
        Assert.False(rig.Angry.IsSessionActive);
        Assert.False(rig.Timer.ControlsLocked);
        Assert.False(rig.Timer.IsRunning);
    }

    [Fact]
    public void CurrentPhrase_is_the_stored_phrase_only_while_a_session_is_active()
    {
        using var rig = new Rig();
        Assert.Equal(Phrase, rig.Angry.NewPhrase());
        Assert.Null(rig.Angry.CurrentPhrase);

        rig.Angry.Engage(1);
        Assert.Equal(Phrase, rig.Angry.CurrentPhrase);

        rig.Angry.TryUnlock(Phrase);
        Assert.Null(rig.Angry.CurrentPhrase);
    }

    [Fact]
    public void Default_phrase_picker_issues_a_known_phrase()
    {
        var scheduler = new ManualScheduler();
        using var timer = new PomodoroTimer(scheduler, () => new IslandSettings(), () => Start0 + scheduler.Now);
        using var angry = new AngryPomodoro(timer);

        Assert.Contains(angry.NewPhrase(), UnlockPhrases.All);
    }
}

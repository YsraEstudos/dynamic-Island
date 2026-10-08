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

        /// <summary>Every value <see cref="AngryPomodoro.IsLocked"/> took, in order, one per LockChanged.</summary>
        public List<bool> LockEvents { get; } = new();

        public Rig(IslandSettings? settings = null)
        {
            var current = settings ?? Minutes(focus: 25);
            Timer = new PomodoroTimer(Scheduler, () => current, () => Start0 + Scheduler.Now);
            Angry = new AngryPomodoro(Timer, () => Phrase);
            Angry.LockChanged += () => LockEvents.Add(Angry.IsLocked);
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

        rig.Angry.Engage();

        Assert.True(rig.Angry.IsLocked);
        Assert.True(rig.Timer.IsRunning);
        Assert.True(rig.Timer.ControlsLocked);
        Assert.Equal(new[] { true }, rig.LockEvents);
    }

    [Fact]
    public void Engage_from_break_switches_to_focus_first()
    {
        using var rig = new Rig(Minutes(focus: 25, breakMinutes: 5));
        rig.Timer.SetPhase(PomodoroPhase.Break);

        rig.Angry.Engage();

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

        rig.Angry.Engage();

        Assert.True(rig.Angry.IsLocked);
        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25) - TimeSpan.FromSeconds(90), rig.Timer.Remaining);
    }

    [Fact]
    public void Second_Engage_is_a_no_op()
    {
        using var rig = new Rig();
        rig.Angry.Engage();
        var remaining = rig.Timer.Remaining;

        rig.Angry.Engage();

        Assert.Equal(new[] { true }, rig.LockEvents);
        Assert.Equal(1, rig.Scheduler.PendingCount);
        Assert.Equal(remaining, rig.Timer.Remaining);
    }

    [Fact]
    public void Locked_timer_cannot_be_paused_or_reset()
    {
        using var rig = new Rig();
        rig.Angry.Engage();
        rig.Advance(5);

        rig.Timer.Pause();
        rig.Timer.Reset();

        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25) - TimeSpan.FromSeconds(5), rig.Timer.Remaining);
        Assert.True(rig.Angry.IsLocked);
    }

    [Fact]
    public void Natural_focus_completion_disarms_and_leaves_controls_free()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 5));
        rig.Angry.Engage();

        rig.Advance(60);

        Assert.False(rig.Angry.IsLocked);
        Assert.Null(rig.Angry.CurrentPhrase);
        Assert.False(rig.Timer.ControlsLocked);
        Assert.Equal(PomodoroPhase.Break, rig.Timer.Phase);
        Assert.Equal(new[] { true, false }, rig.LockEvents);
    }

    [Fact]
    public void Wrong_phrase_keeps_the_lock()
    {
        using var rig = new Rig();
        rig.Angry.Engage();
        rig.Angry.NewPhrase();

        Assert.False(rig.Angry.TryUnlock("Eu desisto do meu foco agora, e sei disso"));
        Assert.False(rig.Angry.TryUnlock("eu desisto do meu foco agora, e sei disso."));
        Assert.False(rig.Angry.TryUnlock(null));

        Assert.True(rig.Angry.IsLocked);
        Assert.True(rig.Timer.ControlsLocked);
        Assert.Equal(new[] { true }, rig.LockEvents);
    }

    [Fact]
    public void Right_phrase_unlocks_and_resets_the_timer_to_full_duration()
    {
        using var rig = new Rig();
        rig.Angry.Engage();
        rig.Advance(30);
        rig.Angry.NewPhrase();

        Assert.True(rig.Angry.TryUnlock(Phrase));

        Assert.False(rig.Angry.IsLocked);
        Assert.False(rig.Timer.ControlsLocked);
        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25), rig.Timer.Remaining);
        Assert.Equal(new[] { true, false }, rig.LockEvents);
    }

    [Fact]
    public void Unlocked_timer_accepts_pause_and_reset_again()
    {
        using var rig = new Rig();
        rig.Angry.Engage();
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
    public void TryUnlock_when_not_locked_is_true()
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
        rig.Angry.Engage();

        rig.Angry.Dispose();

        Assert.False(rig.Angry.IsLocked);
        Assert.False(rig.Timer.ControlsLocked);
        rig.Timer.Pause();
        Assert.False(rig.Timer.IsRunning);
    }

    [Fact]
    public void Engage_after_Dispose_is_a_no_op()
    {
        using var rig = new Rig();
        rig.Angry.Dispose();

        rig.Angry.Engage();

        Assert.False(rig.Angry.IsLocked);
        Assert.False(rig.Timer.ControlsLocked);
        Assert.False(rig.Timer.IsRunning);
    }

    [Fact]
    public void CurrentPhrase_is_the_last_issued_phrase_only_while_locked()
    {
        using var rig = new Rig();
        Assert.Equal(Phrase, rig.Angry.NewPhrase());
        Assert.Null(rig.Angry.CurrentPhrase);

        rig.Angry.Engage();
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

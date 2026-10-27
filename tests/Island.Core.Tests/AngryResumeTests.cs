using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Pomodoro;

namespace Island.Core.Tests;

/// <summary>An Angry session saved before a restart comes back with its timer, lock, phrase and ownership.</summary>
public class AngryResumeTests
{
    // A Tuesday, so the weekly block below falls on today.
    private static readonly DateTime Origin = new(2026, 10, 27, 9, 55, 0);

    private sealed class Rig : IDisposable
    {
        private readonly DateTime _origin;

        public Rig(DateTime origin)
        {
            _origin = origin;
            var settings = new IslandSettings { PomodoroFocusMinutes = 1, PomodoroBreakMinutes = 1, PomodoroAutoAdvance = true };
            Timer = new PomodoroTimer(Scheduler, () => settings, () => Now);
            Angry = new AngryPomodoro(Timer, () => "Eu desisto do meu foco agora, e sei disso.");
            Weekly = new PomodoroWeeklySchedule(Timer, Angry, Scheduler, () => _origin + Scheduler.Now);
        }

        public ManualScheduler Scheduler { get; } = new();
        public PomodoroTimer Timer { get; }
        public AngryPomodoro Angry { get; }
        public PomodoroWeeklySchedule Weekly { get; }

        public DateTimeOffset Now => new(_origin + Scheduler.Now, TimeSpan.Zero);

        public void Advance(TimeSpan span) => Scheduler.Advance(span);

        public void Dispose()
        {
            Weekly.Dispose();
            Angry.Dispose();
            Timer.Dispose();
        }
    }

    [Fact]
    public void A_locked_focus_comes_back_with_its_remaining_time_and_lock()
    {
        using var before = new Rig(Origin);
        before.Angry.Engage(2);
        before.Advance(TimeSpan.FromSeconds(20));

        AngryResumeState? saved = before.Angry.CaptureSession();
        Assert.NotNull(saved);
        Assert.True(saved.Locked);

        // The restart happens ten minutes later on the clock; the saved time does not count that gap.
        using var after = new Rig(Origin.AddMinutes(10));
        after.Angry.RestoreSession(saved);

        Assert.True(after.Angry.IsSessionActive);
        Assert.True(after.Angry.IsLocked);
        Assert.Equal(saved.Phrase, after.Angry.CurrentPhrase);
        Assert.True(after.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(40), after.Timer.Remaining);
        Assert.Equal(1, after.Timer.Cycle);
        Assert.Equal(2, after.Timer.TotalCycles);

        // The restored focus ends on schedule, and the free break after it is not locked.
        after.Advance(TimeSpan.FromSeconds(40));

        Assert.Equal(PomodoroPhase.Break, after.Timer.Phase);
        Assert.True(after.Angry.IsSessionActive);
        Assert.False(after.Angry.IsLocked);
    }

    [Fact]
    public void A_free_break_comes_back_as_a_free_break()
    {
        using var before = new Rig(Origin);
        before.Angry.Engage(2);
        before.Advance(TimeSpan.FromSeconds(70));

        AngryResumeState? saved = before.Angry.CaptureSession();
        Assert.NotNull(saved);
        Assert.False(saved.Locked);

        using var after = new Rig(Origin.AddMinutes(2));
        after.Angry.RestoreSession(saved);

        Assert.Equal(PomodoroPhase.Break, after.Timer.Phase);
        Assert.Equal(TimeSpan.FromSeconds(50), after.Timer.Remaining);
        Assert.True(after.Timer.IsRunning);
        Assert.True(after.Angry.IsSessionActive);
        Assert.False(after.Angry.IsLocked);
    }

    [Fact]
    public void Nothing_is_captured_without_an_active_session()
    {
        using var rig = new Rig(Origin);

        Assert.Null(rig.Angry.CaptureSession());
    }

    [Fact]
    public void A_weekly_session_restored_after_a_restart_still_ends_at_its_block_end()
    {
        // The block runs 10:00 to 10:08. The restart happens at 10:01, during the first free break.
        using var before = new Rig(Origin);
        Assert.True(before.Weekly.TryAdd(Origin.DayOfWeek, new TimeOnly(10, 0), new TimeOnly(10, 8), ScheduledStartMode.Angry,
            1, 1, "Estudo", out _));
        before.Advance(TimeSpan.FromMinutes(6));

        AngryResumeState? saved = before.Angry.CaptureSession();
        Assert.NotNull(saved);
        Assert.True(saved.Scheduled);

        // The session is restored before the block is added again, the same order the app uses at startup. The
        // routine must not start a second session, and the block's end must still stop the restored one.
        using var after = new Rig(Origin.AddMinutes(6));
        after.Angry.RestoreSession(saved);
        Assert.True(after.Weekly.TryAdd(Origin.DayOfWeek, new TimeOnly(10, 0), new TimeOnly(10, 8), ScheduledStartMode.Angry,
            1, 1, "Estudo", out _));
        Assert.True(after.Angry.IsScheduledSession);

        after.Advance(TimeSpan.FromMinutes(2));
        Assert.True(after.Angry.IsSessionActive);

        after.Advance(TimeSpan.FromMinutes(5));
        Assert.False(after.Angry.IsSessionActive);
    }
}

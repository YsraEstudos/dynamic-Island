using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Pomodoro;

namespace Island.Core.Tests;

public class AngryDesktopLockTests
{
    private static readonly DateTimeOffset Start0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    private const string Phrase = "Eu desisto do meu foco agora, e sei disso.";

    private sealed class Rig : IDisposable
    {
        public ManualScheduler Scheduler { get; } = new();
        public FakeVirtualDesktopService Desktops { get; } = new(count: 3);
        public PomodoroTimer Timer { get; }
        public AngryPomodoro Angry { get; }
        public AngryDesktopLock Lock { get; }

        public Rig(IslandSettings? settings = null)
        {
            var current = settings ?? new IslandSettings { PomodoroFocusMinutes = 25, PomodoroBreakMinutes = 5 };
            Timer = new PomodoroTimer(Scheduler, () => current, () => Start0 + Scheduler.Now);
            Angry = new AngryPomodoro(Timer, () => Phrase);
            Lock = new AngryDesktopLock(Angry, Desktops, Scheduler);
        }

        // The fake starts on Start. Target is the desktop chosen for Angry; Away is a third one.
        public Guid Start => Desktops.IdAt(0);
        public Guid Target => Desktops.IdAt(1);
        public Guid Away => Desktops.IdAt(2);
        public Guid? Current => Desktops.GetCurrentDesktopId();

        public void Advance(double seconds) => Scheduler.Advance(TimeSpan.FromSeconds(seconds));

        public void Dispose()
        {
            Lock.Dispose();
            Angry.Dispose();
            Timer.Dispose();
        }
    }

    private static IslandSettings Minutes(int focus, int breakMinutes) =>
        new() { PomodoroFocusMinutes = focus, PomodoroBreakMinutes = breakMinutes };

    [Fact]
    public void No_desktop_chosen_never_switches_and_adds_no_sampling()
    {
        using var rig = new Rig();

        rig.Angry.Engage(1);
        rig.Desktops.UserSwitchTo(rig.Away);
        rig.Advance(30);

        Assert.False(rig.Lock.IsHolding);
        Assert.Equal(0, rig.Desktops.SwitchCount);
        Assert.Equal(1, rig.Scheduler.PendingCount); // the timer's own tick only
    }

    [Fact]
    public void Locked_focus_starts_on_the_chosen_desktop_at_once_and_samples_while_held()
    {
        using var rig = new Rig();
        rig.Lock.SetTargetDesktop(rig.Target);

        rig.Angry.Engage(1);

        Assert.True(rig.Lock.IsHolding);
        Assert.Equal(rig.Target, rig.Current);
        Assert.Equal(1, rig.Desktops.SwitchCount);
        Assert.Equal(2, rig.Scheduler.PendingCount); // timer tick + one sample
    }

    [Fact]
    public void Choosing_a_desktop_during_a_locked_focus_moves_there()
    {
        using var rig = new Rig();
        rig.Angry.Engage(1);

        rig.Lock.SetTargetDesktop(rig.Target);

        Assert.Equal(rig.Target, rig.Current);
        Assert.Equal(1, rig.Desktops.SwitchCount);
    }

    [Fact]
    public void Switching_away_is_undone_about_two_seconds_later()
    {
        using var rig = new Rig();
        rig.Desktops.UserSwitchTo(rig.Target);
        rig.Lock.SetTargetDesktop(rig.Target);
        rig.Angry.Engage(1);

        rig.Desktops.UserSwitchTo(rig.Away);
        rig.Advance(0.5);   // first sample sees the switch; the return is due 2 s after it
        Assert.Equal(rig.Away, rig.Current);

        rig.Advance(1.999); // 2499 ms
        Assert.Equal(rig.Away, rig.Current);

        rig.Advance(0.001); // 2500 ms
        Assert.Equal(rig.Target, rig.Current);
        Assert.Equal(1, rig.Desktops.SwitchCount);
    }

    [Fact]
    public void Returning_in_time_cancels_the_pending_return()
    {
        using var rig = new Rig();
        rig.Desktops.UserSwitchTo(rig.Target);
        rig.Lock.SetTargetDesktop(rig.Target);
        rig.Angry.Engage(1);

        rig.Desktops.UserSwitchTo(rig.Away);
        rig.Advance(0.5);
        rig.Desktops.UserSwitchTo(rig.Target);
        rig.Advance(5);

        Assert.Equal(rig.Target, rig.Current);
        Assert.Equal(0, rig.Desktops.SwitchCount);
    }

    [Fact]
    public void Leaving_again_after_being_taken_back_is_taken_back_again()
    {
        using var rig = new Rig();
        rig.Desktops.UserSwitchTo(rig.Target);
        rig.Lock.SetTargetDesktop(rig.Target);
        rig.Angry.Engage(1);
        rig.Desktops.UserSwitchTo(rig.Away);
        rig.Advance(2.5);
        Assert.Equal(1, rig.Desktops.SwitchCount);

        rig.Desktops.UserSwitchTo(rig.Away);
        rig.Advance(2.5);

        Assert.Equal(rig.Target, rig.Current);
        Assert.Equal(2, rig.Desktops.SwitchCount);
    }

    [Fact]
    public void Break_is_free_and_the_chosen_desktop_is_held_again_with_the_next_focus()
    {
        using var rig = new Rig(Minutes(focus: 1, breakMinutes: 2));
        rig.Desktops.UserSwitchTo(rig.Target);
        rig.Lock.SetTargetDesktop(rig.Target);
        rig.Angry.Engage(3);

        rig.Advance(60); // focus ends: the break starts and the lock lifts
        Assert.False(rig.Angry.IsLocked);
        Assert.False(rig.Lock.IsHolding);
        rig.Desktops.UserSwitchTo(rig.Away);
        rig.Advance(30);

        Assert.Equal(rig.Away, rig.Current);
        Assert.Equal(0, rig.Desktops.SwitchCount);
        Assert.Equal(1, rig.Scheduler.PendingCount); // no sampling during the break

        rig.Advance(90); // 180 s: the break ends and the next focus locks

        Assert.Equal(rig.Target, rig.Current);
        Assert.Equal(1, rig.Desktops.SwitchCount);
    }

    [Fact]
    public void Ending_the_session_releases_the_desktop_and_stops_sampling()
    {
        using var rig = new Rig();
        rig.Desktops.UserSwitchTo(rig.Target);
        rig.Lock.SetTargetDesktop(rig.Target);
        rig.Angry.Engage(1);
        rig.Desktops.UserSwitchTo(rig.Away);
        rig.Advance(0.5);

        rig.Angry.NewPhrase();
        Assert.True(rig.Angry.TryUnlock(Phrase));
        rig.Advance(5);

        Assert.False(rig.Lock.IsHolding);
        Assert.Equal(rig.Away, rig.Current);
        Assert.Equal(0, rig.Desktops.SwitchCount);
        Assert.Equal(0, rig.Scheduler.PendingCount); // timer reset, sample and return gone
    }

    [Fact]
    public void Clearing_the_desktop_releases_the_hold()
    {
        using var rig = new Rig();
        rig.Desktops.UserSwitchTo(rig.Target);
        rig.Lock.SetTargetDesktop(rig.Target);
        rig.Angry.Engage(1);

        rig.Lock.SetTargetDesktop(null);
        rig.Desktops.UserSwitchTo(rig.Away);
        rig.Advance(5);

        Assert.False(rig.Lock.IsHolding);
        Assert.Null(rig.Lock.TargetDesktopId);
        Assert.Equal(rig.Away, rig.Current);
        Assert.Equal(0, rig.Desktops.SwitchCount);
        Assert.Equal(1, rig.Scheduler.PendingCount);
    }

    [Fact]
    public void Retargeting_while_held_moves_at_once_and_cancels_the_old_return()
    {
        using var rig = new Rig();
        rig.Desktops.UserSwitchTo(rig.Target);
        rig.Lock.SetTargetDesktop(rig.Target);
        rig.Angry.Engage(1);
        rig.Desktops.UserSwitchTo(rig.Start);
        rig.Advance(0.5); // a return to the old target is now pending

        rig.Lock.SetTargetDesktop(rig.Away);

        Assert.Equal(rig.Away, rig.Current);
        Assert.Equal(1, rig.Desktops.SwitchCount);

        rig.Advance(3);

        Assert.Equal(rig.Away, rig.Current);
        Assert.Equal(1, rig.Desktops.SwitchCount);
    }

    [Fact]
    public void Unavailable_desktops_never_hold()
    {
        using var rig = new Rig();
        rig.Desktops.IsAvailable = false;

        rig.Lock.SetTargetDesktop(rig.Target);
        rig.Angry.Engage(1);
        rig.Advance(5);

        Assert.False(rig.Lock.IsHolding);
        Assert.Equal(0, rig.Desktops.SwitchCount);
        Assert.Equal(1, rig.Scheduler.PendingCount);
    }

    [Fact]
    public void Unreadable_current_desktop_is_left_alone()
    {
        using var rig = new Rig();
        rig.Desktops.UserSwitchTo(rig.Target);
        rig.Lock.SetTargetDesktop(rig.Target);
        rig.Angry.Engage(1);
        rig.Desktops.CurrentReadable = false;
        rig.Desktops.UserSwitchTo(rig.Away);

        rig.Advance(5);

        Assert.Equal(0, rig.Desktops.SwitchCount);
        Assert.True(rig.Lock.IsHolding);
    }

    [Fact]
    public void Dispose_stops_holding_and_its_timers()
    {
        using var rig = new Rig();
        rig.Desktops.UserSwitchTo(rig.Target);
        rig.Lock.SetTargetDesktop(rig.Target);
        rig.Angry.Engage(1);

        rig.Lock.Dispose();
        rig.Desktops.UserSwitchTo(rig.Away);
        rig.Advance(5);

        Assert.False(rig.Lock.IsHolding);
        Assert.Equal(0, rig.Desktops.SwitchCount);
        Assert.Equal(1, rig.Scheduler.PendingCount);
    }

    [Fact]
    public void Saved_desktop_id_round_trips_and_bad_text_means_no_desktop()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, AngryDesktopLock.ParseDesktopId(AngryDesktopLock.FormatDesktopId(id)));
        Assert.Equal(string.Empty, AngryDesktopLock.FormatDesktopId(null));
        Assert.Null(AngryDesktopLock.ParseDesktopId(string.Empty));
        Assert.Null(AngryDesktopLock.ParseDesktopId(null));
        Assert.Null(AngryDesktopLock.ParseDesktopId("não é um id"));
    }
}

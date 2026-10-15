using Island.Core.Abstractions;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Pomodoro;
using Island.Windows.Pomodoro;

namespace Island.Windows.Tests;

public sealed class PomodoroScheduleStoreTests
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

        public Rig(IslandSettings? settings = null, string? storePath = null)
        {
            var current = settings ?? Minutes(focus: 25);
            Timer = new PomodoroTimer(Scheduler, () => current, () => Start0 + Scheduler.Now);
            Angry = new AngryPomodoro(Timer, () => Phrase);
            Schedule = new PomodoroSchedule(Timer, Angry, Scheduler, () => Now, storePath is null ? null : new JsonPomodoroScheduleStore(storePath));
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

    /// <summary>A temporary store file, deleted with its temporary sibling when disposed.</summary>
    private sealed class TempStore : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pomodoro-schedule-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            File.Delete(Path);
            File.Delete(Path + ".tmp");
        }
    }

    private static IslandSettings Minutes(int focus, int breakMinutes = 5) =>
        new() { PomodoroFocusMinutes = focus, PomodoroBreakMinutes = breakMinutes };

    private static string SavedEntry(DateTimeOffset at, string mode, int cycles) =>
        $"{{ \"id\": \"{Guid.NewGuid()}\", \"at\": \"{at:O}\", \"mode\": \"{mode}\", \"cycles\": {cycles} }}";

    private static void WriteStore(string path, params string[] entries) =>
        File.WriteAllText(path, "{ \"starts\": [" + string.Join(",", entries) + "] }");

    [Fact]
    public void Starts_survive_a_restart_when_saved_to_a_store()
    {
        using var store = new TempStore();
        Guid id;
        ScheduledStart added;
        using (var first = new Rig(storePath: store.Path))
        {
            added = first.Schedule.Add(first.Now.AddDays(1), ScheduledStartMode.Angry, 3)!;
            id = added.Id;
        }

        using var second = new Rig(storePath: store.Path);

        var loaded = Assert.Single(second.Schedule.Starts);
        Assert.Equal(added, loaded);
        Assert.Equal(id, loaded.Id);
        Assert.Equal(loaded, second.Schedule.Pending);
    }

    [Fact]
    public void Removing_and_cancelling_are_saved_to_the_store()
    {
        using var store = new TempStore();
        using (var first = new Rig(storePath: store.Path))
        {
            var keep = first.Schedule.Add(first.Now.AddDays(1), ScheduledStartMode.Focus, 1)!;
            var drop = first.Schedule.Add(first.Now.AddDays(2), ScheduledStartMode.Focus, 1)!;
            first.Schedule.Remove(drop.Id);
        }

        using (var second = new Rig(storePath: store.Path))
        {
            Assert.Single(second.Schedule.Starts);
            second.Schedule.Cancel();
        }

        using var third = new Rig(storePath: store.Path);
        Assert.Empty(third.Schedule.Starts);
    }

    [Fact]
    public void Load_drops_a_start_more_than_four_hours_old()
    {
        using var store = new TempStore();
        WriteStore(store.Path, SavedEntry(Start0.AddHours(-4).AddMinutes(-1), "Focus", 2));

        using var rig = new Rig(storePath: store.Path);
        rig.Advance(0);

        Assert.Empty(rig.Schedule.Starts);
        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(0, rig.Scheduler.PendingCount);
    }

    [Fact]
    public void Load_fires_a_start_that_is_a_few_minutes_late_right_away()
    {
        using var store = new TempStore();
        WriteStore(store.Path, SavedEntry(Start0.AddMinutes(-5), "Focus", 2));

        using var rig = new Rig(storePath: store.Path);
        Assert.Single(rig.Schedule.Starts);

        rig.Advance(0);

        Assert.True(rig.Timer.IsRunning);
        Assert.Equal(2, rig.Timer.TotalCycles);
        Assert.Empty(rig.Schedule.Starts);
    }

    [Fact]
    public void Load_fires_a_start_missed_while_the_app_was_closed_for_half_an_hour()
    {
        // Scheduled for 8:00 and the app is opened at 8:30: the start is 30 minutes past due when the store loads.
        // The rig clock is Start0, so the saved start sits 30 minutes before it.
        using var store = new TempStore();
        WriteStore(store.Path, SavedEntry(Start0.AddMinutes(-30), "Focus", 2));

        using var rig = new Rig(storePath: store.Path);
        Assert.Single(rig.Schedule.Starts);

        rig.Advance(0);

        Assert.True(rig.Timer.IsRunning);
        Assert.True(rig.Timer.PlanActive);
        Assert.Equal(2, rig.Timer.TotalCycles);
        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.Empty(rig.Schedule.Starts);

        // The fired start is removed from the store, so a later launch does not run it again.
        using var later = new Rig(storePath: store.Path);
        Assert.Empty(later.Schedule.Starts);
    }

    [Fact]
    public void A_broken_store_file_gives_no_starts()
    {
        using var store = new TempStore();
        File.WriteAllText(store.Path, "{ not json at all");

        using var rig = new Rig(storePath: store.Path);

        Assert.Empty(rig.Schedule.Starts);
        Assert.Null(rig.Schedule.Pending);
    }

}

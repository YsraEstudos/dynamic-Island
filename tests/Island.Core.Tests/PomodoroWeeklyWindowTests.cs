using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Pomodoro;

namespace Island.Core.Tests;

/// <summary>Weekly blocks and one-off starts that carry their own focus and break lengths and end on time.</summary>
public class PomodoroWeeklyWindowTests
{
    // A Monday, so the block on today's weekday starts five minutes into the run.
    private static readonly DateTime Origin = new(2026, 10, 26, 9, 55, 0);
    private static readonly DayOfWeek Day = Origin.DayOfWeek;

    private sealed class Rig : IDisposable
    {
        public ManualScheduler Scheduler { get; } = new();
        public IslandSettings Settings { get; set; } = new() { PomodoroFocusMinutes = 1, PomodoroBreakMinutes = 1 };
        public PomodoroTimer Timer { get; }
        public AngryPomodoro Angry { get; }
        public PomodoroSchedule Schedule { get; }
        public PomodoroWeeklySchedule Weekly { get; }

        public Rig()
        {
            Timer = new PomodoroTimer(Scheduler, () => Settings, () => Now);
            Angry = new AngryPomodoro(Timer, () => "Eu desisto do meu foco agora, e sei disso.");
            Schedule = new PomodoroSchedule(Timer, Angry, Scheduler, () => Now);
            Weekly = new PomodoroWeeklySchedule(Timer, Angry, Scheduler, () => Origin + Scheduler.Now);
        }

        public DateTimeOffset Now => new(Origin + Scheduler.Now, TimeSpan.Zero);

        public void Advance(TimeSpan span) => Scheduler.Advance(span);

        public void Dispose()
        {
            Weekly.Dispose();
            Schedule.Dispose();
            Angry.Dispose();
            Timer.Dispose();
        }
    }

    private static TimeOnly At(int hour, int minute) => new(hour, minute);

    [Theory]
    [InlineData(120, 25, 5, 4)]
    [InlineData(119, 25, 5, 3)]
    [InlineData(29, 25, 5, 0)]
    [InlineData(0, 25, 5, 0)]
    [InlineData(100000, 1, 1, PomodoroTimer.MaxCycles)]
    public void CyclesFor_counts_whole_focus_and_break_pairs(int windowMinutes, int focus, int brk, int expected) =>
        Assert.Equal(expected, PomodoroTimer.CyclesFor(TimeSpan.FromMinutes(windowMinutes), focus, brk));

    [Fact]
    public void A_weekly_block_sizes_its_plan_to_the_window()
    {
        using var rig = new Rig();
        Assert.True(rig.Weekly.TryAdd(Day, At(10, 0), At(10, 8), ScheduledStartMode.Focus, 1, 1, "Estudo", out _));

        rig.Advance(TimeSpan.FromMinutes(5));

        Assert.True(rig.Timer.PlanActive);
        Assert.Equal(4, rig.Timer.TotalCycles);
    }

    [Fact]
    public void A_weekly_block_stops_at_its_end_even_when_a_paused_plan_is_still_running()
    {
        using var rig = new Rig();
        Assert.True(rig.Weekly.TryAdd(Day, At(10, 0), At(10, 8), ScheduledStartMode.Focus, 1, 1, "Estudo", out _));
        rig.Advance(TimeSpan.FromMinutes(5));
        rig.Advance(TimeSpan.FromSeconds(30));
        rig.Timer.Pause();

        rig.Advance(TimeSpan.FromMinutes(10));

        Assert.False(rig.Timer.PlanActive);
        Assert.False(rig.Timer.IsRunning);
    }

    [Fact]
    public void A_weekly_block_keeps_its_own_break_when_the_settings_change()
    {
        using var rig = new Rig();
        Assert.True(rig.Weekly.TryAdd(Day, At(10, 0), At(10, 8), ScheduledStartMode.Focus, 1, 1, "Estudo", out _));
        rig.Advance(TimeSpan.FromMinutes(5));

        rig.Settings = new IslandSettings { PomodoroFocusMinutes = 1, PomodoroBreakMinutes = 30 };
        rig.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
        Assert.Equal(2, rig.Timer.Cycle);
    }

    [Fact]
    public void A_plan_reports_the_break_it_captured_when_it_started()
    {
        using var rig = new Rig();
        rig.Timer.StartPlan(2, null, 25, 5);

        rig.Settings = new IslandSettings { PomodoroFocusMinutes = 1, PomodoroBreakMinutes = 30 };

        Assert.Equal(TimeSpan.FromMinutes(5), rig.Timer.PlanBreakDuration);
    }

    [Fact]
    public void A_block_too_short_for_one_pomodoro_is_rejected()
    {
        using var rig = new Rig();

        Assert.False(rig.Weekly.TryAdd(Day, At(10, 0), At(10, 1), ScheduledStartMode.Focus, 25, 5, "Curto", out string error));

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Empty(rig.Weekly.Blocks);
    }

    [Fact]
    public void Editing_a_block_keeps_its_id_and_rejects_an_overlap()
    {
        using var rig = new Rig();
        Assert.True(rig.Weekly.TryAdd(Day, At(10, 0), At(11, 0), ScheduledStartMode.Focus, 25, 5, "A", out _));
        Assert.True(rig.Weekly.TryAdd(Day, At(14, 0), At(15, 0), ScheduledStartMode.Focus, 25, 5, "B", out _));
        Guid id = rig.Weekly.Blocks.Single(block => block.Name == "A").Id;

        Assert.True(rig.Weekly.TryUpdate(id, Day, At(10, 0), At(11, 0), ScheduledStartMode.Angry, 10, 5, "A2", out _));
        PomodoroWeeklyBlock edited = rig.Weekly.Blocks.Single(block => block.Id == id);
        Assert.Equal("A2", edited.Name);
        Assert.Equal(ScheduledStartMode.Angry, edited.Mode);
        Assert.Equal(10, edited.FocusMinutes);

        Assert.False(rig.Weekly.TryUpdate(id, Day, At(13, 30), At(14, 30), ScheduledStartMode.Focus, 25, 5, "A3", out string error));
        Assert.Contains("sobrepõe", error);
    }

    [Fact]
    public void A_scheduled_start_runs_its_own_lengths_and_can_be_edited_before_it_fires()
    {
        using var rig = new Rig();
        ScheduledStart start = rig.Schedule.Add(rig.Now.AddMinutes(5), rig.Now.AddMinutes(35), ScheduledStartMode.Focus, 25, 5)!;
        Assert.Equal(1, start.Cycles);
        Assert.Equal(rig.Now.AddMinutes(35), start.End);

        Assert.True(rig.Schedule.Update(start.Id, rig.Now.AddMinutes(10), rig.Now.AddMinutes(22),
            ScheduledStartMode.Angry, 5, 1));
        ScheduledStart updated = rig.Schedule.Starts.Single();
        Assert.Equal(start.Id, updated.Id);
        Assert.Equal(ScheduledStartMode.Angry, updated.Mode);
        Assert.Equal(2, updated.Cycles);

        rig.Advance(TimeSpan.FromMinutes(10));

        Assert.True(rig.Timer.PlanActive);
        Assert.Equal(TimeSpan.FromMinutes(5), rig.Timer.PhaseDuration);
        Assert.True(rig.Angry.IsSessionActive);
    }

    [Fact]
    public void A_scheduled_start_cannot_be_moved_into_the_past()
    {
        using var rig = new Rig();
        ScheduledStart start = rig.Schedule.Add(rig.Now.AddMinutes(5), ScheduledStartMode.Focus, 1)!;

        Assert.False(rig.Schedule.Update(start.Id, rig.Now.AddMinutes(-1), rig.Now.AddMinutes(10), ScheduledStartMode.Focus, 25, 5));
        Assert.Equal(start.Id, rig.Schedule.Starts.Single().Id);
    }
}

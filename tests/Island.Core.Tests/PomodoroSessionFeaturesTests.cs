using System.Text.Json;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Pomodoro;

namespace Island.Core.Tests;

public sealed class PomodoroSessionFeaturesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Manual_boundaries_keep_the_plan_and_only_lock_angry_after_Start(bool angryMode)
    {
        var settings = JsonSerializer.Deserialize<IslandSettings>("""{"PomodoroAutoAdvance":false,"PomodoroFocusMinutes":1,"PomodoroBreakMinutes":1}""")!;
        var scheduler = new ManualScheduler();
        using var timer = new PomodoroTimer(scheduler, () => settings, () => DateTimeOffset.UnixEpoch + scheduler.Now);
        using var angry = new AngryPomodoro(timer);
        if (angryMode) angry.Engage(2); else timer.StartPlan(2);
        scheduler.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(PomodoroPhase.Break, timer.Phase);
        Assert.False(timer.IsRunning);
        Assert.True(timer.PlanActive);
        if (angryMode) Assert.True(angry.IsSessionActive);
        Assert.False(angry.IsLocked);
        scheduler.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(TimeSpan.FromMinutes(1), timer.Remaining);
        timer.Start();
        scheduler.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(PomodoroPhase.Focus, timer.Phase);
        Assert.Equal(2, timer.Cycle);
        Assert.False(timer.IsRunning);
        Assert.False(angry.IsLocked);
        timer.Start();
        Assert.Equal(angryMode, angry.IsLocked);
        scheduler.Advance(TimeSpan.FromMinutes(1));
        Assert.False(timer.IsRunning);
        timer.Start();
        scheduler.Advance(TimeSpan.FromMinutes(1));
        Assert.False(timer.PlanActive);
        Assert.False(angry.IsSessionActive);
    }

    [Fact]
    public void Preparation_warns_once_per_focus_even_after_pause_and_resume()
    {
        var settings = new IslandSettings { PomodoroFocusMinutes = 1 };
        var scheduler = new ManualScheduler();
        using var timer = new PomodoroTimer(scheduler, () => settings, () => DateTimeOffset.UnixEpoch + scheduler.Now);
        var warningEvent = typeof(PomodoroTimer).GetEvent("BreakApproaching");
        Assert.NotNull(warningEvent);
        int warnings = 0;
        warningEvent.AddEventHandler(timer, (Action)(() => warnings++));
        timer.StartPlan(1);
        scheduler.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(0, warnings);
        scheduler.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, warnings);
        timer.Pause();
        scheduler.Advance(TimeSpan.FromSeconds(60));
        timer.Start();
        scheduler.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(1, warnings);
        timer.Reset();
        timer.StartPlan(1);
        scheduler.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(2, warnings);
    }

    [Fact]
    public void The_goal_is_captured_when_starting_and_survives_manual_boundaries()
    {
        var settings = JsonSerializer.Deserialize<IslandSettings>("""{"PomodoroGoal":"Revisar matemática","PomodoroAutoAdvance":false,"PomodoroFocusMinutes":1}""")!;
        var scheduler = new ManualScheduler();
        using var timer = new PomodoroTimer(scheduler, () => settings, () => DateTimeOffset.UnixEpoch + scheduler.Now);
        var goal = typeof(PomodoroTimer).GetProperty("SessionGoal");
        Assert.NotNull(goal);
        timer.StartPlan(2);
        settings = new IslandSettings();
        scheduler.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("Revisar matemática", goal.GetValue(timer));
    }

    [Fact]
    public void Scheduled_angry_start_captures_goal_and_keeps_manual_boundaries()
    {
        var settings = new IslandSettings { PomodoroAutoAdvance = false, PomodoroFocusMinutes = 1,
            PomodoroBreakMinutes = 1, PomodoroGoal = "Revisar matemática" };
        var scheduler = new ManualScheduler();
        Func<DateTimeOffset> clock = () => DateTimeOffset.UnixEpoch + scheduler.Now;
        using var timer = new PomodoroTimer(scheduler, () => settings, clock);
        using var angry = new AngryPomodoro(timer);
        using var schedule = new PomodoroSchedule(timer, angry, scheduler, clock);
        schedule.Add(clock() + TimeSpan.FromSeconds(5), ScheduledStartMode.Angry, 2);
        scheduler.Advance(TimeSpan.FromSeconds(5));
        Assert.True(timer.IsRunning);
        Assert.True(angry.IsLocked);
        Assert.Equal("Revisar matemática", timer.SessionGoal);
        settings = settings with { PomodoroGoal = "Next session", PomodoroFocusMinutes = 50, PomodoroBreakMinutes = 10, PomodoroAutoAdvance = true };
        Assert.False(timer.RefreshConfigurationIfIdle());
        scheduler.Advance(TimeSpan.FromMinutes(1));
        Assert.False(timer.IsRunning);
        Assert.True(timer.IsAwaitingNextPhase);
        Assert.True(angry.IsSessionActive);
        Assert.Equal(TimeSpan.FromMinutes(1), timer.PhaseDuration);
        timer.Start(); scheduler.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(PomodoroPhase.Focus, timer.Phase);
        Assert.Equal(TimeSpan.FromMinutes(1), timer.PhaseDuration);
        Assert.Equal("Revisar matemática", timer.SessionGoal);
        Assert.False(timer.IsRunning);
        Assert.False(angry.IsLocked);
    }

    [Theory]
    [InlineData(50, 1)]
    [InlineData(65, 0)]
    public void Delayed_ticks_warn_only_if_focus_is_still_running(int elapsedSeconds, int expectedWarnings)
    {
        var scheduler = new ManualScheduler();
        var now = DateTimeOffset.UnixEpoch;
        using var timer = new PomodoroTimer(scheduler, () => new IslandSettings { PomodoroFocusMinutes = 1 }, () => now);
        int warnings = 0;
        timer.BreakApproaching += () => warnings++;
        timer.StartPlan(1);
        now += TimeSpan.FromSeconds(elapsedSeconds);
        scheduler.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(expectedWarnings, warnings);
        Assert.Equal(elapsedSeconds < 60 ? PomodoroPhase.Focus : PomodoroPhase.Break, timer.Phase);
    }
}

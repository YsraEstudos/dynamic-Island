using System.Text.Json;
using Island.Core.Pomodoro;
using Island.Windows.Guardian;
using Island.Windows.Pomodoro;

namespace Island.Windows.Tests;

public sealed class AngryLiveStateStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 14, 0, 0, TimeSpan.FromHours(-3));
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"angry-live-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        TryDelete(_path);
        TryDelete(_path + ".bad");
        TryDelete(_path + ".tmp");
    }

    private static PomodoroTimerState Timer(TimeSpan? remaining = null) => new(
        PomodoroPhase.Focus,
        TimeSpan.FromMinutes(25),
        remaining ?? TimeSpan.FromMinutes(20),
        Running: true,
        PlanTotal: 4,
        PlanCycle: 2,
        PlanFocusDuration: TimeSpan.FromMinutes(25),
        PlanBreakDuration: TimeSpan.FromMinutes(5),
        AutoAdvance: true,
        SessionGoal: "Revisar matemática",
        AwaitingNextPhase: false);

    private static AngryResumeState Session(bool locked = true) =>
        new(Timer(), locked, "Eu desisto do meu foco agora, e sei disso.", Scheduled: false);

    private static AngryLiveState Live(DateTimeOffset savedAt, bool active = true, bool locked = true, bool phoneBlock = true) =>
        new(savedAt, active, locked, Scheduled: false, "frase", Timer(), TimeSpan.FromMinutes(20), phoneBlock);

    [Fact]
    public void A_saved_state_round_trips_with_every_field()
    {
        var store = new AngryLiveStateStore(_path);
        AngryLiveState state = AngryLiveState.FromSession(Session(), Now, phoneBlockEnabled: true);

        Assert.True(store.Save(state));
        AngryLiveState? read = store.Read();

        Assert.NotNull(read);
        Assert.Equal(state.SavedAtUtc, read.SavedAtUtc);
        Assert.True(read.Active);
        Assert.True(read.Locked);
        Assert.False(read.Scheduled);
        Assert.Equal(state.Phrase, read.Phrase);
        Assert.True(read.PhoneBlockEnabled);
        Assert.Equal(state.Timer.Phase, read.Timer.Phase);
        Assert.Equal(state.Timer.Remaining, read.Timer.Remaining);
        Assert.Equal(state.Timer.PlanTotal, read.Timer.PlanTotal);
        Assert.Equal(state.Timer.PlanCycle, read.Timer.PlanCycle);
        Assert.Equal(state.Timer.SessionGoal, read.Timer.SessionGoal);
        // endMs/endUtc are the session end; both are derived from the saved instant plus the forecast.
        Assert.Equal(state.SessionEndUtc, read.SessionEndUtc);
        Assert.Equal(state.EndMs, read.EndMs);
    }

    [Fact]
    public void The_file_keeps_the_flat_shape_sysguard_reads()
    {
        new AngryLiveStateStore(_path).Save(AngryLiveState.FromSession(Session(), Now, phoneBlockEnabled: true));
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(_path));
        JsonElement root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.True(root.GetProperty("active").GetBoolean());
        Assert.True(root.GetProperty("locked").GetBoolean());
        Assert.True(root.GetProperty("phoneBlockEnabled").GetBoolean());
        Assert.True(root.GetProperty("savedAtMs").GetInt64() > 0);
        Assert.True(root.GetProperty("endMs").GetInt64() > root.GetProperty("savedAtMs").GetInt64());
        Assert.Equal(Now, root.GetProperty("savedAtUtc").GetDateTimeOffset());
        Assert.Equal("Focus", root.GetProperty("timer").GetProperty("phase").GetString());
        Assert.Equal(TimeSpan.FromMinutes(20).TotalMilliseconds,
            root.GetProperty("timer").GetProperty("remainingMs").GetInt64());
    }

    [Fact]
    public void A_recent_running_state_is_restorable_and_the_file_is_kept()
    {
        var store = new AngryLiveStateStore(_path);
        store.Save(AngryLiveState.FromSession(Session(), Now, phoneBlockEnabled: true));

        AngryLiveState? taken = store.TakeRestorable(Now.AddMinutes(2));

        Assert.NotNull(taken);
        Assert.True(taken.Active);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void A_state_older_than_ten_minutes_is_not_restored_and_is_removed()
    {
        var store = new AngryLiveStateStore(_path);
        store.Save(AngryLiveState.FromSession(Session(), Now, phoneBlockEnabled: true));

        Assert.Null(store.TakeRestorable(Now + TimeSpan.FromMinutes(11)));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void A_state_whose_session_already_ended_is_not_restored()
    {
        var store = new AngryLiveStateStore(_path);
        store.Save(Live(Now.AddMinutes(-2)) with { SessionRemaining = TimeSpan.FromMinutes(1) });

        Assert.Null(store.TakeRestorable(Now));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void An_inactive_state_is_not_restored()
    {
        var store = new AngryLiveStateStore(_path);
        store.Save(AngryLiveState.Inactive(Timer(), Now, phoneBlockEnabled: true));

        Assert.False(store.Read()!.IsRestorable(Now, AngryLiveState.RestoreMaxAge));
        Assert.Null(store.TakeRestorable(Now));
    }

    [Fact]
    public void A_corrupted_file_is_ignored_and_removed_without_throwing()
    {
        File.WriteAllText(_path, "{ not json");
        var store = new AngryLiveStateStore(_path);

        Assert.Null(store.Read());
        Assert.Null(store.TakeRestorable(Now));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void A_state_with_an_impossible_timer_is_not_restored()
    {
        var store = new AngryLiveStateStore(_path);
        // Remaining longer than the phase duration: corrupted or hand-edited, must not be resumed.
        store.Save(Live(Now) with { Timer = Timer(TimeSpan.FromHours(3)) });

        Assert.Null(store.TakeRestorable(Now));
    }

    [Fact]
    public void A_missing_file_reads_as_null()
    {
        var store = new AngryLiveStateStore(_path);

        Assert.Null(store.Read());
        Assert.Null(store.TakeRestorable(Now));
    }

    [Fact]
    public void A_break_snapshot_releases_twitter_and_instagram_only_in_its_final_minute()
    {
        var store = new AngryLiveStateStore(_path);
        AngryLiveState early = AngryLiveState.FromSession(
            BreakSession(TimeSpan.FromMinutes(2)), Now, phoneBlockEnabled: true);
        Assert.False(early.IsBreakReleaseNow(Now));

        AngryLiveState final = AngryLiveState.FromSession(
            BreakSession(TimeSpan.FromSeconds(45)), Now, phoneBlockEnabled: true);
        Assert.True(final.IsBreakReleaseNow(Now));
        // The snapshot ages with wall time: a dead island cannot release the sites for longer than the minute.
        Assert.True(final.IsBreakReleaseNow(Now.AddSeconds(44)));
        Assert.False(final.IsBreakReleaseNow(Now.AddSeconds(45)));

        Assert.True(store.Save(final));
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(_path));
        Assert.True(doc.RootElement.GetProperty("breakReleaseOpen").GetBoolean());
    }

    [Fact]
    public void A_break_waiting_for_manual_start_releases_nothing()
    {
        AngryLiveState waiting = AngryLiveState.FromSession(
            BreakSession(TimeSpan.FromSeconds(30), running: false), Now, phoneBlockEnabled: true);

        Assert.False(waiting.IsBreakReleaseNow(Now));
    }

    private static AngryResumeState BreakSession(TimeSpan remaining, bool running = true) =>
        new(Timer() with
        {
            Phase = PomodoroPhase.Break,
            PhaseDuration = TimeSpan.FromMinutes(5),
            Remaining = remaining,
            Running = running,
        }, Locked: false, "Eu desisto do meu foco agora, e sei disso.", Scheduled: false);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class GuardianPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 14, 0, 0, TimeSpan.Zero);

    private static PomodoroTimerState Timer() => new(PomodoroPhase.Focus, TimeSpan.FromMinutes(25),
        TimeSpan.FromMinutes(20), true, 3, 1, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), true, "", false);

    private static AngryLiveState State(bool active = true, bool locked = true, TimeSpan? remaining = null) =>
        new(Now, active, locked, false, "frase", Timer(), remaining ?? TimeSpan.FromMinutes(20), true);

    private static AngryLiveState BreakState(TimeSpan timerRemaining, bool running = true, DateTimeOffset? savedAt = null) =>
        new(savedAt ?? Now, Active: true, Locked: false, Scheduled: false, "frase",
            Timer() with
            {
                Phase = PomodoroPhase.Break,
                PhaseDuration = TimeSpan.FromMinutes(5),
                Remaining = timerRemaining,
                Running = running,
            },
            TimeSpan.FromMinutes(10), true);

    [Fact]
    public void The_guardian_invocation_is_parsed_only_with_a_pid_and_a_state_path()
    {
        Assert.True(GuardianPolicy.TryParseArguments(["--guardian", "1234", @"C:\temp\angry-live.json"], out int pid,
            out string path));
        Assert.Equal(1234, pid);
        Assert.True(Path.IsPathFullyQualified(path));

        Assert.False(GuardianPolicy.TryParseArguments(["--guardian"], out _, out _));
        Assert.False(GuardianPolicy.TryParseArguments(["--guardian", "abc", @"C:\x.json"], out _, out _));
        Assert.False(GuardianPolicy.TryParseArguments(["--guardian", "0", @"C:\x.json"], out _, out _));
        Assert.False(GuardianPolicy.TryParseArguments(["--guardian", "12", " "], out _, out _));
        Assert.False(GuardianPolicy.TryParseArguments(["--demo"], out _, out _));
        Assert.True(GuardianPolicy.IsInvocation(["--guardian", "12", "x"]));
        Assert.False(GuardianPolicy.IsInvocation(["--GUARDIAN"]));
    }

    [Fact]
    public void The_island_is_reopened_while_a_session_is_running()
    {
        Assert.True(GuardianPolicy.ShouldRelaunch(State(), Now));
        Assert.True(GuardianPolicy.ShouldRelaunch(State(locked: false), Now)); // a break also counts
        Assert.False(GuardianPolicy.ShouldRelaunch(State(active: false), Now));
        Assert.False(GuardianPolicy.ShouldRelaunch(null, Now));
    }

    [Fact]
    public void An_ended_session_ends_the_guardian()
    {
        Assert.True(GuardianPolicy.ShouldExit(State(active: false), Now));
        Assert.True(GuardianPolicy.ShouldExit(State(remaining: TimeSpan.Zero), Now));
        Assert.True(GuardianPolicy.ShouldExit(null, Now));
        Assert.False(GuardianPolicy.ShouldExit(State(), Now));
    }

    [Fact]
    public void The_guardian_blocks_every_site_through_the_break_except_the_final_minute()
    {
        Assert.Equal(Island.Windows.Focus.SiteGuardMode.All, GuardianPolicy.TitleGuardMode(State(locked: true), Now));

        // A break is guarded like the focus until its last minute, when only Twitter/X and Instagram open.
        Assert.Equal(Island.Windows.Focus.SiteGuardMode.All,
            GuardianPolicy.TitleGuardMode(BreakState(TimeSpan.FromSeconds(61)), Now));
        Assert.Equal(Island.Windows.Focus.SiteGuardMode.BreakRelease,
            GuardianPolicy.TitleGuardMode(BreakState(TimeSpan.FromSeconds(60)), Now));

        // A break waiting for manual Start, and an expired snapshot, release nothing.
        Assert.Equal(Island.Windows.Focus.SiteGuardMode.All,
            GuardianPolicy.TitleGuardMode(BreakState(TimeSpan.FromSeconds(30), running: false), Now));
        Assert.Equal(Island.Windows.Focus.SiteGuardMode.All,
            GuardianPolicy.TitleGuardMode(BreakState(TimeSpan.FromSeconds(45), savedAt: Now.AddSeconds(-46)), Now));
    }

    [Fact]
    public void A_running_pid_is_alive_and_an_unused_one_is_not()
    {
        Assert.True(GuardianPolicy.IsProcessAlive(Environment.ProcessId));
        Assert.False(GuardianPolicy.IsProcessAlive(0));
        Assert.False(GuardianPolicy.IsProcessAlive(int.MaxValue - 1));
    }
}

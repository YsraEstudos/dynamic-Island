using System.Text.Json;
using System.Text.Json.Serialization;
using Island.Core.Pomodoro;
using Island.Windows.Storage;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Pomodoro;

/// <summary>
/// Reads and writes <c>angry-live.json</c> (format documented on <see cref="AngryLiveState"/>). The file is written
/// atomically, so a reader never sees a half-written state, and every failure is swallowed: the live state is a
/// safety net, never a reason to interrupt the Angry session. A stale or corrupted file is removed on the startup read.
/// </summary>
public sealed class AngryLiveStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly ILogger<AngryLiveStateStore>? _log;

    /// <param name="path">Defaults to <c>%LocalAppData%\DynamicIsland\angry-live.json</c>.</param>
    /// <param name="log">Optional; only used to trace a failed write.</param>
    public AngryLiveStateStore(string? path = null, ILogger<AngryLiveStateStore>? log = null)
    {
        Path = path ?? DefaultPath;
        _log = log;
    }

    /// <summary>The canonical location both the island and SysGuard read.</summary>
    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "angry-live.json");

    /// <summary>Full path of the state file.</summary>
    public string Path { get; }

    /// <summary>Writes the state atomically. Returns false (and logs) when the file could not be written.</summary>
    public bool Save(AngryLiveState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            AtomicJsonFile.Write(Path, StoreFile.From(state), Options);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log?.LogWarning(ex, "Could not write the live Angry state to {Path}", Path);
            return false;
        }
    }

    /// <summary>
    /// Reads the state without touching the file. Null when the file is missing, unreadable or not a valid document.
    /// </summary>
    public AngryLiveState? Read()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            return Parse(File.ReadAllText(Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the state for a normal startup. Returns it only when it is still resumable (<see cref="AngryLiveState.IsRestorable"/>);
    /// otherwise removes the file (a stale or corrupted leftover must not linger) and returns null.
    /// </summary>
    public AngryLiveState? TakeRestorable(DateTimeOffset now, TimeSpan? maxAge = null)
    {
        AngryLiveState? state = Read();
        if (state is not null && state.IsRestorable(now, maxAge ?? AngryLiveState.RestoreMaxAge)) return state;

        if (state is not null || File.Exists(Path)) Discard();
        return null;
    }

    /// <summary>Removes the file. Used when a state is stale or when no session may be resumed.</summary>
    public void Discard()
    {
        try { File.Delete(Path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static AngryLiveState? Parse(string json)
    {
        StoreFile? file = JsonSerializer.Deserialize<StoreFile>(json, Options);
        if (file is null) return null;

        AngryLiveState? timerState = null;
        if (file.Timer is { } timer)
        {
            timerState = new AngryLiveState(
                file.SavedAtUtc,
                file.Active,
                file.Locked,
                file.Scheduled,
                file.Phrase,
                new PomodoroTimerState(
                    timer.GetPhase(),
                    FromMilliseconds(timer.PhaseDurationMs),
                    FromMilliseconds(timer.RemainingMs),
                    timer.Running,
                    timer.PlanTotal,
                    timer.PlanCycle,
                    FromMilliseconds(timer.PlanFocusMs),
                    FromMilliseconds(timer.PlanBreakMs),
                    timer.AutoAdvance,
                    timer.SessionGoal ?? string.Empty,
                    timer.AwaitingNextPhase),
                TimeSpan.Zero,
                file.PhoneBlockEnabled);
        }

        // The session end is the authority (SysGuard reads endMs); derive the remaining from it so both agree.
        TimeSpan remaining = TimeSpan.Zero;
        if (file.EndUtc is { } end)
        {
            remaining = end - file.SavedAtUtc;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        }
        else if (timerState is not null)
        {
            remaining = timerState.Timer.Remaining;
        }

        if (timerState is null)
        {
            // A file without the timer block still tells the guardian whether to reopen the island; carry zeroed values.
            return new AngryLiveState(file.SavedAtUtc, file.Active, file.Locked, file.Scheduled, file.Phrase,
                new PomodoroTimerState(PomodoroPhase.Focus, TimeSpan.Zero, TimeSpan.Zero, false, 0, 0,
                    TimeSpan.Zero, TimeSpan.Zero, true, string.Empty, false), remaining, file.PhoneBlockEnabled);
        }

        return timerState with { SessionRemaining = remaining };
    }

    private static TimeSpan FromMilliseconds(long milliseconds)
    {
        if (milliseconds <= 0) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(milliseconds > (long)TimeSpan.FromHours(24).TotalMilliseconds
            ? TimeSpan.FromHours(24).TotalMilliseconds
            : milliseconds);
    }

    /// <summary>On-disk shape (version 1). Kept separate from the in-memory record so the JSON stays SysGuard-friendly.</summary>
    private sealed class StoreFile
    {
        public int Version { get; set; } = 1;
        public DateTimeOffset SavedAtUtc { get; set; }
        public long SavedAtMs { get; set; }
        public bool Active { get; set; }
        public bool Locked { get; set; }
        public bool Scheduled { get; set; }
        public bool PhoneBlockEnabled { get; set; }
        // True only inside the final minute of a running break: Twitter/X and Instagram are released then. Consumers
        // recompute this from the timer block and savedAt, so a stale file cannot prolong the release.
        public bool BreakReleaseOpen { get; set; }
        public string? Phrase { get; set; }
        public DateTimeOffset? EndUtc { get; set; }
        public long EndMs { get; set; }
        public TimerFile? Timer { get; set; }

        public static StoreFile From(AngryLiveState state) => new()
        {
            Version = 1,
            SavedAtUtc = state.SavedAtUtc,
            SavedAtMs = state.SavedAtMs,
            Active = state.Active,
            Locked = state.Locked,
            Scheduled = state.Scheduled,
            PhoneBlockEnabled = state.PhoneBlockEnabled,
            BreakReleaseOpen = state.IsBreakReleaseNow(state.SavedAtUtc),
            Phrase = state.Phrase,
            EndUtc = state.Active ? state.SessionEndUtc : null,
            EndMs = state.EndMs,
            Timer = TimerFile.From(state.Timer),
        };
    }

    private sealed class TimerFile
    {
        public string Phase { get; set; } = nameof(PomodoroPhase.Focus);
        public long PhaseDurationMs { get; set; }
        public long RemainingMs { get; set; }
        public bool Running { get; set; }
        public int PlanTotal { get; set; }
        public int PlanCycle { get; set; }
        public long PlanFocusMs { get; set; }
        public long PlanBreakMs { get; set; }
        public bool AutoAdvance { get; set; }
        public string? SessionGoal { get; set; }
        public bool AwaitingNextPhase { get; set; }

        public static TimerFile From(PomodoroTimerState timer) => new()
        {
            Phase = timer.Phase.ToString(),
            PhaseDurationMs = ToMilliseconds(timer.PhaseDuration),
            RemainingMs = ToMilliseconds(timer.Remaining),
            Running = timer.Running,
            PlanTotal = timer.PlanTotal,
            PlanCycle = timer.PlanCycle,
            PlanFocusMs = ToMilliseconds(timer.PlanFocusDuration),
            PlanBreakMs = ToMilliseconds(timer.PlanBreakDuration),
            AutoAdvance = timer.AutoAdvance,
            SessionGoal = timer.SessionGoal,
            AwaitingNextPhase = timer.AwaitingNextPhase,
        };

        public PomodoroPhase GetPhase() =>
            Enum.TryParse(Phase, ignoreCase: true, out PomodoroPhase phase) ? phase : PomodoroPhase.Focus;

        private static long ToMilliseconds(TimeSpan span) =>
            span < TimeSpan.Zero ? 0 : (long)Math.Round(span.TotalMilliseconds);
    }
}

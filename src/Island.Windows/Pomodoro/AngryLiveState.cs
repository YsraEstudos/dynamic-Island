using Island.Core.Pomodoro;

namespace Island.Windows.Pomodoro;

/// <summary>
/// The live Angry state mirrored to disk (<c>%LocalAppData%\DynamicIsland\angry-live.json</c>). It is written on every
/// lock/session change and every 5 s while a session is active, so other processes can take over when the island dies:
/// the guardian (<c>DynamicIsland.exe --guardian &lt;pid&gt; &lt;stateFile&gt;</c>) reopens the island and keeps the
/// browser title guard running, and SysGuard reads it to keep the distracting sites in the hosts file while the
/// session runs. The JSON has flat, self-describing fields on purpose; SysGuard parses it without sharing code.
/// <para>
/// Format (version 1, camelCase):
/// <code>
/// {
///   "version": 1,
///   "savedAtUtc": "2026-11-11T17:14:32.1234567+00:00",
///   "savedAtMs": 1762881272123,
///   "active": true,
///   "locked": true,
///   "scheduled": false,
///   "phoneBlockEnabled": true,
///   "breakReleaseOpen": false,
///   "phrase": "Eu desisto do meu foco agora, e sei disso.",
///   "endUtc": "2026-11-11T17:38:12.1234567+00:00",
///   "endMs": 1762882692123,
///   "timer": {
///     "phase": "Focus", "phaseDurationMs": 1500000, "remainingMs": 1420000, "running": true,
///     "planTotal": 4, "planCycle": 2, "planFocusMs": 1500000, "planBreakMs": 300000,
///     "autoAdvance": true, "sessionGoal": "Revisar matemática", "awaitingNextPhase": false
///   }
/// }
/// </code>
/// <c>endMs</c>/<c>endUtc</c> are the estimated end of the whole Angry session (the rest of the plan), in epoch
/// milliseconds UTC; <c>savedAtMs</c> is the freshness signal (written at most 5 s apart while active);
/// <c>locked</c> is false during a break of the plan while <c>active</c> stays true; <c>breakReleaseOpen</c> is only
/// written true inside the final minute of a running break, when Twitter/X and Instagram are released (the consumers
/// recompute it from the timer block so a stale file cannot release them for longer than that minute). <c>phoneBlockEnabled</c> records
/// that the phone block was on when the session started, so disabling it in settings.json and killing the island cannot
/// quietly turn the phone block off for the next focus: the resume path restores it. When the session ends the file is
/// rewritten once with <c>active=false</c> because SysGuard uses its existence and flags to apply the extra blocking.
/// </para>
/// </summary>
public sealed record AngryLiveState(
    DateTimeOffset SavedAtUtc,
    bool Active,
    bool Locked,
    bool Scheduled,
    string? Phrase,
    PomodoroTimerState Timer,
    TimeSpan SessionRemaining,
    bool PhoneBlockEnabled)
{
    /// <summary>Oldest saved state taken at startup: an island that was closed for longer than this is not resumed.</summary>
    public static readonly TimeSpan RestoreMaxAge = TimeSpan.FromMinutes(10);

    /// <summary>Estimated end of the whole Angry session, as an absolute time. Never before <see cref="SavedAtUtc"/>.</summary>
    public DateTimeOffset SessionEndUtc => SavedAtUtc + (SessionRemaining > TimeSpan.Zero ? SessionRemaining : TimeSpan.Zero);

    /// <summary>Epoch milliseconds UTC, the unit SysGuard reads.</summary>
    public long SavedAtMs => SavedAtUtc.ToUnixTimeMilliseconds();

    /// <summary>Epoch milliseconds UTC, the unit SysGuard reads.</summary>
    public long EndMs => SessionEndUtc.ToUnixTimeMilliseconds();

    /// <summary>The final minute of a running break is the only window in which Twitter/X and Instagram are released.</summary>
    public static readonly TimeSpan BreakReleaseWindow = AngryPomodoro.BreakReleaseWindow;

    /// <summary>
    /// True while the island releases Twitter/X and Instagram: the final minute of a running break of a live session,
    /// with the timer controls and cycle count still protected. The decision is recomputed from the saved timer and the
    /// elapsed time, so a snapshot left behind by a dead island stops releasing the sites when the minute ends. A break
    /// waiting for manual Start is not running and releases nothing.
    /// </summary>
    public bool IsBreakReleaseNow(DateTimeOffset now)
    {
        if (!IsGuardable(now) || Locked) return false;
        if (Timer.Phase != PomodoroPhase.Break || !Timer.Running) return false;
        TimeSpan remaining = RemainingTimerNow(now);
        return remaining > TimeSpan.Zero && remaining <= BreakReleaseWindow;
    }

    /// <summary>Timer remaining projected to <paramref name="now"/> from the saved snapshot (never negative).</summary>
    public TimeSpan RemainingTimerNow(DateTimeOffset now)
    {
        TimeSpan elapsed = now - SavedAtUtc;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        TimeSpan remaining = Timer.Remaining - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>Snapshots an active Angry session; the session remaining comes from the plan forecast.</summary>
    public static AngryLiveState FromSession(AngryResumeState session, DateTimeOffset now, bool phoneBlockEnabled)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new AngryLiveState(now, Active: true, session.Locked, session.Scheduled, session.Phrase,
            session.Timer, EstimateRemaining(session.Timer), phoneBlockEnabled);
    }

    /// <summary>Snapshot of "no Angry session" (the timer fields are kept for diagnostics).</summary>
    public static AngryLiveState Inactive(PomodoroTimerState timer, DateTimeOffset now, bool phoneBlockEnabled)
    {
        ArgumentNullException.ThrowIfNull(timer);
        return new AngryLiveState(now, Active: false, Locked: false, Scheduled: false, Phrase: null, timer,
            TimeSpan.Zero, phoneBlockEnabled);
    }

    /// <summary>
    /// True when this state can be resumed by a fresh island process: a session that is really active, saved recently
    /// (a lookup file left over from days ago is not taken), still running (its end is in the future) and with timer
    /// values that make sense. A state failing this is ignored and removed.
    /// </summary>
    public bool IsRestorable(DateTimeOffset now, TimeSpan maxAge)
    {
        TimeSpan age = now - SavedAtUtc;
        if (age < TimeSpan.Zero || age > maxAge) return false;
        return IsGuardable(now) && IsConsistent(Timer);
    }

    /// <summary>
    /// True while there is an Angry session to protect: active and its estimated end has not passed. The guardian uses
    /// it to decide whether to reopen the island and whether the browser title guard should stay on.
    /// </summary>
    public bool IsGuardable(DateTimeOffset now) => Active && SessionEndUtc > now;

    /// <summary>True when the session is at a locked focus (the strict guard: every blocked site).</summary>
    public bool IsLockedNow(DateTimeOffset now) => IsGuardable(now) && Locked;

    /// <summary>The resume record this state represents, for <see cref="AngryPomodoro.RestoreSession"/>.</summary>
    public AngryResumeState ToResumeState() => new(Timer, Locked, Phrase, Scheduled);

    /// <summary>
    /// Sanity check for the timer part, so corrupted or hand-edited JSON is not restored. A running timer can never
    /// have more time left than its phase length (a 1 s tolerance covers rounding at phase boundaries).
    /// </summary>
    public static bool IsConsistent(PomodoroTimerState timer)
    {
        if (timer.PhaseDuration < TimeSpan.Zero || timer.PhaseDuration > TimeSpan.FromHours(24)) return false;
        if (timer.Remaining < TimeSpan.Zero || timer.Remaining > timer.PhaseDuration + TimeSpan.FromSeconds(1)) return false;
        if (timer.PlanTotal < 0 || timer.PlanTotal > PomodoroTimer.MaxCycles) return false;
        if (timer.PlanTotal == 0 ? timer.PlanCycle != 0 : timer.PlanCycle < 1 || timer.PlanCycle > timer.PlanTotal)
            return false;
        if (timer.PlanFocusDuration < TimeSpan.Zero || timer.PlanFocusDuration > TimeSpan.FromHours(24)) return false;
        if (timer.PlanBreakDuration < TimeSpan.Zero || timer.PlanBreakDuration > TimeSpan.FromHours(24)) return false;
        return true;
    }

    /// <summary>How long the whole session still takes: the rest of the plan, or just the phase when there is no plan.</summary>
    private static TimeSpan EstimateRemaining(PomodoroTimerState timer)
    {
        if (timer.PlanTotal <= 0) return timer.Remaining > TimeSpan.Zero ? timer.Remaining : TimeSpan.Zero;

        TimeSpan remaining = PomodoroForecast.Remaining(timer.Phase, timer.Remaining, Math.Max(1, timer.PlanCycle),
            timer.PlanTotal, timer.PlanFocusDuration, timer.PlanBreakDuration);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}

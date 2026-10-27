namespace Island.Core.Pomodoro;

/// <summary>
/// The timer as it was when captured, enough to continue it after a restart. <see cref="Remaining"/> is the time left at
/// capture; the time the app spent closed does not count.
/// </summary>
public sealed record PomodoroTimerState(
    PomodoroPhase Phase,
    TimeSpan PhaseDuration,
    TimeSpan Remaining,
    bool Running,
    int PlanTotal,
    int PlanCycle,
    TimeSpan PlanFocusDuration,
    TimeSpan PlanBreakDuration,
    bool AutoAdvance,
    string SessionGoal,
    bool AwaitingNextPhase);

/// <summary>
/// An active Angry session: its timer, whether the focus was locked, the unlock phrase to type, and whether the weekly
/// routine started it (so the block's end still stops it).
/// </summary>
public sealed record AngryResumeState(PomodoroTimerState Timer, bool Locked, string? Phrase, bool Scheduled);

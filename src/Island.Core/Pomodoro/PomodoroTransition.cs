namespace Island.Core.Pomodoro;

/// <summary>Describes a focus/break boundary reached by the countdown running out.</summary>
/// <param name="Ended">Phase that just ended.</param>
/// <param name="Next">Phase the timer switched to.</param>
/// <param name="Cycle">1-based pomodoro of the plan the ended phase belonged to; 0 when no plan was active.</param>
/// <param name="TotalCycles">Pomodoros in the plan; 0 when no plan was active.</param>
/// <param name="PlanFinished">True when the last break of the plan ended (the timer stays paused on Focus).</param>
/// <param name="AutoStarted">True when the next phase started counting by itself.</param>
/// <param name="NextDuration">Full length of the next phase.</param>
public sealed record PomodoroTransition(PomodoroPhase Ended, PomodoroPhase Next, int Cycle, int TotalCycles, bool PlanFinished, bool AutoStarted, TimeSpan NextDuration);

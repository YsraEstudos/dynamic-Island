namespace Island.Core.Pomodoro;

/// <summary>Pure arithmetic for how long pomodoro plans still take. Holds no state and reads no clock.</summary>
public static class PomodoroForecast
{
    /// <summary>
    /// Time left in a running plan: the rest of the current phase, plus the break that follows the current focus,
    /// plus one full focus/break pair for every pomodoro after the current one.
    /// </summary>
    /// <param name="phase">Phase running now. A break or prep counts only its own remaining time, plus the later pomodoros.</param>
    /// <param name="phaseRemaining">Time left in <paramref name="phase"/>; negative values count as zero.</param>
    /// <param name="cycle">1-based pomodoro that is running now.</param>
    /// <param name="total">Pomodoros in the plan.</param>
    /// <param name="focus">Length of a focus phase.</param>
    /// <param name="breakLength">Length of a break phase.</param>
    public static TimeSpan Remaining(PomodoroPhase phase, TimeSpan phaseRemaining, int cycle, int total, TimeSpan focus, TimeSpan breakLength)
    {
        var left = phaseRemaining < TimeSpan.Zero ? TimeSpan.Zero : phaseRemaining;
        if (phase == PomodoroPhase.Focus) left += breakLength;
        return left + Pomodoros(Math.Max(0, total - cycle), focus, breakLength);
    }

    /// <summary>Time a fresh plan of <paramref name="cycles"/> pomodoros takes, from its first focus to its last break.</summary>
    /// <param name="cycles">Pomodoros in the plan; negative values count as zero.</param>
    /// <param name="focus">Length of a focus phase.</param>
    /// <param name="breakLength">Length of a break phase.</param>
    public static TimeSpan ForNewPlan(int cycles, TimeSpan focus, TimeSpan breakLength) =>
        Pomodoros(Math.Max(0, cycles), focus, breakLength);

    private static TimeSpan Pomodoros(int count, TimeSpan focus, TimeSpan breakLength) =>
        TimeSpan.FromTicks((focus + breakLength).Ticks * count);
}

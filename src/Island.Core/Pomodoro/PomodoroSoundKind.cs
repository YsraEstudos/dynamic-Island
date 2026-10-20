using Island.Core.Configuration;

namespace Island.Core.Pomodoro;

public enum PomodoroSoundKind { SoftBell, Piano, Digital, Coffee }

public static class PomodoroSoundSelection
{
    public static PomodoroSoundKind? For(PomodoroTransition transition, IslandSettings settings)
    {
        if (!settings.PomodoroSound) return null;
        // The final break ends the session; use the relaxed cue rather than announce another focus.
        return transition.PlanFinished || transition.Next == PomodoroPhase.Break
            ? Normalize(settings.PomodoroBreakSound, PomodoroSoundKind.Coffee)
            : Normalize(settings.PomodoroFocusSound, PomodoroSoundKind.SoftBell);
    }

    public static PomodoroSoundKind Normalize(PomodoroSoundKind sound, PomodoroSoundKind fallback) =>
        Enum.IsDefined(sound) ? sound : fallback;
}

using Island.Core.Models;

namespace Island.Core.Pomodoro;

/// <summary>Toast texts for pomodoro phase changes. Always urgent, so the island opens even over the shelf or the Mini pill.</summary>
public static class PomodoroNotices
{
    public static Notice For(PomodoroTransition t)
    {
        var minutes = (int)Math.Round(t.NextDuration.TotalMinutes);

        if (t.Ended == PomodoroPhase.Prep)
            return new Notice("Hora de estudar", $"Foco de {minutes} min", "timer", Urgent: true);

        if (t.Ended == PomodoroPhase.Focus)
        {
            var subtitle = t.TotalCycles > 0
                ? $"Pomodoro {t.Cycle} de {t.TotalCycles} feito · pausa de {minutes} min"
                : "Pomodoro concluído";
            return new Notice("Hora do descanso", subtitle, "timer", Urgent: true);
        }

        if (t.PlanFinished)
        {
            var subtitle = t.TotalCycles == 1 ? "1 pomodoro feito" : $"{t.TotalCycles} pomodoros feitos";
            return new Notice("Sessão concluída", subtitle, "check", Urgent: true);
        }

        return t.TotalCycles > 0
            ? new Notice("Volte ao foco", $"Pomodoro {t.Cycle + 1} de {t.TotalCycles} · {minutes} min", "timer", Urgent: true)
            : new Notice("Volte ao foco", "Descanso terminado", "timer", Urgent: true);
    }
}

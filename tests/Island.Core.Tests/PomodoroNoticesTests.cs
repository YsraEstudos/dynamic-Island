using Island.Core.Models;
using Island.Core.Pomodoro;

namespace Island.Core.Tests;

public class PomodoroNoticesTests
{
    private static PomodoroTransition Transition(PomodoroPhase ended, PomodoroPhase next, int cycle, int totalCycles,
        bool planFinished, int nextMinutes) =>
        new(ended, next, cycle, totalCycles, planFinished, AutoStarted: false, TimeSpan.FromMinutes(nextMinutes));

    [Fact]
    public void Focus_end_with_plan_announces_the_break()
    {
        var notice = PomodoroNotices.For(Transition(PomodoroPhase.Focus, PomodoroPhase.Break, 2, 4, false, 5));

        Assert.Equal("Hora do descanso", notice.Title);
        Assert.Equal("Pomodoro 2 de 4 feito · pausa de 5 min", notice.Subtitle);
        Assert.Equal("timer", notice.Glyph);
    }

    [Fact]
    public void Focus_end_without_plan_announces_the_break()
    {
        var notice = PomodoroNotices.For(Transition(PomodoroPhase.Focus, PomodoroPhase.Break, 0, 0, false, 5));

        Assert.Equal("Hora do descanso", notice.Title);
        Assert.Equal("Pomodoro concluído", notice.Subtitle);
        Assert.Equal("timer", notice.Glyph);
    }

    [Theory]
    [InlineData(1, "1 pomodoro feito")]
    [InlineData(4, "4 pomodoros feitos")]
    public void Break_end_with_finished_plan_closes_the_session(int totalCycles, string expectedSubtitle)
    {
        var notice = PomodoroNotices.For(Transition(PomodoroPhase.Break, PomodoroPhase.Focus, totalCycles, totalCycles, true, 25));

        Assert.Equal("Sessão concluída", notice.Title);
        Assert.Equal(expectedSubtitle, notice.Subtitle);
        Assert.Equal("check", notice.Glyph);
    }

    [Fact]
    public void Break_end_with_unfinished_plan_announces_the_next_focus()
    {
        var notice = PomodoroNotices.For(Transition(PomodoroPhase.Break, PomodoroPhase.Focus, 2, 4, false, 25));

        Assert.Equal("Volte ao foco", notice.Title);
        Assert.Equal("Pomodoro 3 de 4 · 25 min", notice.Subtitle);
        Assert.Equal("timer", notice.Glyph);
    }

    [Fact]
    public void Break_end_without_plan_announces_the_next_focus()
    {
        var notice = PomodoroNotices.For(Transition(PomodoroPhase.Break, PomodoroPhase.Focus, 0, 0, false, 25));

        Assert.Equal("Volte ao foco", notice.Title);
        Assert.Equal("Descanso terminado", notice.Subtitle);
        Assert.Equal("timer", notice.Glyph);
    }

    [Fact]
    public void Pre_phase_end_announces_the_focus()
    {
        var notice = PomodoroNotices.For(Transition(PomodoroPhase.Prep, PomodoroPhase.Focus, 0, 0, false, 25));

        Assert.Equal("Hora de estudar", notice.Title);
        Assert.Equal("Foco de 25 min", notice.Subtitle);
        Assert.Equal("timer", notice.Glyph);
    }

    [Fact]
    public void Every_pomodoro_notice_is_urgent()
    {
        var transitions = new[]
        {
            Transition(PomodoroPhase.Prep, PomodoroPhase.Focus, 0, 0, false, 25),
            Transition(PomodoroPhase.Focus, PomodoroPhase.Break, 2, 4, false, 5),
            Transition(PomodoroPhase.Focus, PomodoroPhase.Break, 0, 0, false, 5),
            Transition(PomodoroPhase.Break, PomodoroPhase.Focus, 4, 4, true, 25),
            Transition(PomodoroPhase.Break, PomodoroPhase.Focus, 2, 4, false, 25),
            Transition(PomodoroPhase.Break, PomodoroPhase.Focus, 0, 0, false, 25),
        };

        foreach (var transition in transitions)
            Assert.True(PomodoroNotices.For(transition).Urgent);
    }
}

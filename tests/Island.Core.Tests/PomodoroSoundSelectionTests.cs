using Island.Core.Configuration;
using Island.Core.Pomodoro;

namespace Island.Core.Tests;

public sealed class PomodoroSoundSelectionTests
{
    [Theory]
    [InlineData(PomodoroPhase.Focus, PomodoroPhase.Break, false, PomodoroSoundKind.Piano)]
    [InlineData(PomodoroPhase.Break, PomodoroPhase.Focus, false, PomodoroSoundKind.Digital)]
    [InlineData(PomodoroPhase.Prep, PomodoroPhase.Focus, false, PomodoroSoundKind.Digital)]
    [InlineData(PomodoroPhase.Break, PomodoroPhase.Focus, true, PomodoroSoundKind.Piano)]
    public void Each_boundary_uses_the_saved_cue_and_respects_mute(PomodoroPhase ended, PomodoroPhase next,
        bool finished, PomodoroSoundKind expected)
    {
        var settings = new IslandSettings { PomodoroBreakSound = PomodoroSoundKind.Piano, PomodoroFocusSound = PomodoroSoundKind.Digital };
        var transition = new PomodoroTransition(ended, next, 1, 2, finished, !finished, TimeSpan.FromMinutes(5));
        Assert.Equal(expected, PomodoroSoundSelection.For(transition, settings));
        Assert.Null(PomodoroSoundSelection.For(transition, settings with { PomodoroSound = false }));
    }
}

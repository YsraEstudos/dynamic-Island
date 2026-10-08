using Island.Core.Pomodoro;

namespace Island.Core.Tests;

public class PomodoroForecastTests
{
    private static readonly TimeSpan Focus = TimeSpan.FromMinutes(25);
    private static readonly TimeSpan Break = TimeSpan.FromMinutes(5);

    [Fact]
    public void ForNewPlan_of_three_pomodoros_is_one_hour_thirty()
    {
        Assert.Equal(TimeSpan.FromMinutes(90), PomodoroForecast.ForNewPlan(3, Focus, Break));
    }

    [Fact]
    public void ForNewPlan_with_no_cycles_or_negative_cycles_is_zero()
    {
        Assert.Equal(TimeSpan.Zero, PomodoroForecast.ForNewPlan(0, Focus, Break));
        Assert.Equal(TimeSpan.Zero, PomodoroForecast.ForNewPlan(-2, Focus, Break));
    }

    [Fact]
    public void Remaining_on_a_fresh_focus_counts_its_break_and_every_later_pomodoro()
    {
        var left = PomodoroForecast.Remaining(PomodoroPhase.Focus, Focus, 1, 3, Focus, Break);

        Assert.Equal(TimeSpan.FromMinutes(90), left);
    }

    [Fact]
    public void Remaining_on_the_last_focus_counts_only_its_rest_and_its_break()
    {
        var left = PomodoroForecast.Remaining(PomodoroPhase.Focus, TimeSpan.FromMinutes(10), 3, 3, Focus, Break);

        Assert.Equal(TimeSpan.FromMinutes(15), left);
    }

    [Fact]
    public void Remaining_in_a_break_counts_only_the_break_and_later_pomodoros()
    {
        var left = PomodoroForecast.Remaining(PomodoroPhase.Break, TimeSpan.FromMinutes(2), 1, 3, Focus, Break);

        Assert.Equal(TimeSpan.FromMinutes(2 + 2 * 30), left);
    }

    [Fact]
    public void Remaining_in_the_last_break_is_just_that_break()
    {
        var left = PomodoroForecast.Remaining(PomodoroPhase.Break, TimeSpan.FromMinutes(4), 3, 3, Focus, Break);

        Assert.Equal(TimeSpan.FromMinutes(4), left);
    }

    [Fact]
    public void Remaining_in_prep_counts_only_its_own_time_and_later_pomodoros()
    {
        var left = PomodoroForecast.Remaining(PomodoroPhase.Prep, TimeSpan.FromMinutes(5), 1, 2, Focus, Break);

        Assert.Equal(TimeSpan.FromMinutes(5 + 30), left);
    }

    [Fact]
    public void Remaining_treats_a_negative_phase_remainder_as_zero()
    {
        var left = PomodoroForecast.Remaining(PomodoroPhase.Break, TimeSpan.FromMinutes(-3), 2, 2, Focus, Break);

        Assert.Equal(TimeSpan.Zero, left);
    }

    [Fact]
    public void Remaining_never_counts_negative_pomodoros_past_the_total()
    {
        var left = PomodoroForecast.Remaining(PomodoroPhase.Break, TimeSpan.FromMinutes(1), 4, 2, Focus, Break);

        Assert.Equal(TimeSpan.FromMinutes(1), left);
    }
}

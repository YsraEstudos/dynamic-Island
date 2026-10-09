using Island.Core.Budgets;

namespace Island.Core.Tests.Budgets;

public sealed class BudgetPlannerTests
{
    private static Budget ReferenceBudget() => new()
    {
        StartDate = new DateOnly(2026, 10, 9),
        EndDate = new DateOnly(2026, 10, 14),
        ExcludedDays = [new DateOnly(2026, 10, 11)],
        ResetHour = 4,
        TotalPercent = 100,
        Mode = EntryMode.Consumed,
        EnteredValue = 26,
    };

    private static DateTime At(int day, int hour) => new(2026, 10, day, hour, 0, 0);

    [Fact]
    public void Reference_example_gives_18_5_per_day_18_today_and_56_left()
    {
        BudgetPlan plan = BudgetPlanner.Plan(ReferenceBudget(), At(9, 10));

        Assert.Equal(PlanState.Active, plan.State);
        Assert.Equal(4, plan.UsableDaysLeft);
        Assert.Equal(18.5, plan.PerDayPercent, 6);
        Assert.True(plan.TodayIsUsable);
        Assert.Equal(18, plan.TodayAllowance);
        Assert.Equal(56, plan.LeftAfterToday, 6);
    }

    [Fact]
    public void Reference_example_marks_days_with_expected_statuses()
    {
        BudgetPlan plan = BudgetPlanner.Plan(ReferenceBudget(), At(9, 10));

        DayStatus[] expected = [
            DayStatus.Available, DayStatus.Available, DayStatus.Excluded,
            DayStatus.Available, DayStatus.Available, DayStatus.Reset];
        Assert.Equal(expected, plan.Days.Select(d => d.Status));
        Assert.True(plan.Days[0].IsToday);
        Assert.Equal(18.5, plan.Days[0].Percent, 6);
        Assert.Equal(0, plan.Days[2].Percent, 6);
    }

    [Fact]
    public void Excluded_today_gives_zero_allowance_and_keeps_full_remaining()
    {
        Budget budget = ReferenceBudget() with
        {
            ExcludedDays = [new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 11)],
        };

        BudgetPlan plan = BudgetPlanner.Plan(budget, At(9, 10));

        Assert.False(plan.TodayIsUsable);
        Assert.Equal(0, plan.TodayAllowance);
        Assert.Equal(74, plan.LeftAfterToday, 6);
        Assert.Equal(3, plan.UsableDaysLeft);
    }

    [Fact]
    public void Before_start_is_NotStarted_with_no_allowance()
    {
        BudgetPlan plan = BudgetPlanner.Plan(ReferenceBudget(), At(8, 10));

        Assert.Equal(PlanState.NotStarted, plan.State);
        Assert.False(plan.TodayIsUsable);
        Assert.Equal(0, plan.TodayAllowance);
        Assert.Equal(74, plan.LeftAfterToday, 6);
        Assert.Equal(4, plan.UsableDaysLeft);
    }

    [Fact]
    public void After_end_is_Ended_with_no_usable_days()
    {
        BudgetPlan plan = BudgetPlanner.Plan(ReferenceBudget(), At(15, 10));

        Assert.Equal(PlanState.Ended, plan.State);
        Assert.Equal(0, plan.UsableDaysLeft);
        Assert.Equal(0, plan.PerDayPercent, 6);
        Assert.Equal(0, plan.TodayAllowance);
        Assert.Equal(74, plan.LeftAfterToday, 6);
    }

    [Fact]
    public void Two_am_with_reset_hour_4_still_counts_as_previous_day()
    {
        BudgetPlan plan = BudgetPlanner.Plan(ReferenceBudget(), At(10, 2));

        Assert.Equal(new DateOnly(2026, 10, 9), plan.Today);
        Assert.True(plan.Days[0].IsToday);
        Assert.Equal(18, plan.TodayAllowance);
    }

    [Fact]
    public void Four_am_with_reset_hour_4_starts_the_new_day()
    {
        BudgetPlan plan = BudgetPlanner.Plan(ReferenceBudget(), At(10, 4));

        Assert.Equal(new DateOnly(2026, 10, 10), plan.Today);
        Assert.Equal(24, plan.TodayAllowance);
    }

    [Fact]
    public void End_before_start_does_not_throw_and_has_no_usable_days()
    {
        var budget = new Budget { StartDate = new DateOnly(2026, 10, 10), EndDate = new DateOnly(2026, 10, 9) };

        BudgetPlan plan = BudgetPlanner.Plan(budget, At(10, 10));

        Assert.Contains(plan.Days, d => d.Date == new DateOnly(2026, 10, 10));
        Assert.Equal(0, plan.UsableDaysLeft);
        Assert.Equal(0, plan.PerDayPercent, 6);
        Assert.Equal(0, plan.TodayAllowance);
    }

    [Fact]
    public void End_equal_to_start_is_a_single_reset_day()
    {
        var budget = new Budget { StartDate = new DateOnly(2026, 10, 10), EndDate = new DateOnly(2026, 10, 10) };

        BudgetPlan plan = BudgetPlanner.Plan(budget, At(10, 10));

        DayRow day = Assert.Single(plan.Days);
        Assert.Equal(DayStatus.Reset, day.Status);
        Assert.Equal(0, plan.UsableDaysLeft);
    }

    [Fact]
    public void Allowance_is_rounded_down_not_to_nearest()
    {
        var budget = new Budget { StartDate = new DateOnly(2026, 10, 9), EndDate = new DateOnly(2026, 10, 15) };

        BudgetPlan plan = BudgetPlanner.Plan(budget, At(9, 10));

        Assert.Equal(6, plan.UsableDaysLeft);
        Assert.Equal(100.0 / 6, plan.PerDayPercent, 6);
        Assert.Equal(16, plan.TodayAllowance);
        Assert.Equal(84, plan.LeftAfterToday, 6);
    }
}

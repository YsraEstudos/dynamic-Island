using Island.Core.Budgets;

namespace Island.Core.Tests.Budgets;

public sealed class BudgetPlannerResetDayTests
{
    private static Budget Partial(int resetHour, double consumed = 26) => new()
    {
        StartDate = new DateOnly(2026, 10, 9),
        EndDate = new DateOnly(2026, 10, 14),
        ExcludedDays = [new DateOnly(2026, 10, 11)],
        ResetHour = resetHour,
        TotalPercent = 100,
        Mode = EntryMode.Consumed,
        EnteredValue = consumed,
    };

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0);

    [Fact]
    public void Reset_day_counts_as_half_day_when_reset_hour_is_15()
    {
        BudgetPlan plan = BudgetPlanner.Plan(Partial(15), At(9, 10));

        Assert.Equal(4.5, plan.WeightedDaysLeft, 6);
        Assert.Equal(74 / 4.5, plan.PerDayPercent, 6);
        Assert.Equal(74 / 4.5, plan.TodayPercent, 6);
        Assert.Equal(16, plan.TodayAllowance);
        Assert.Equal(58, plan.LeftAfterToday, 6);
        Assert.Equal(DayStatus.Reset, plan.Days[5].Status);
        Assert.Equal(74 / 4.5 * 0.5, plan.Days[5].Percent, 6);
    }

    [Fact]
    public void Reset_hour_8_gives_reset_day_zero_weight()
    {
        BudgetPlan plan = BudgetPlanner.Plan(Partial(8), At(9, 10));

        Assert.Equal(4, plan.WeightedDaysLeft, 6);
        Assert.Equal(18.5, plan.PerDayPercent, 6);
        Assert.Equal(0, plan.Days[5].Percent, 6);
    }

    [Fact]
    public void Reset_hour_9_gives_reset_day_half_weight()
    {
        BudgetPlan plan = BudgetPlanner.Plan(Partial(9), At(9, 10));

        Assert.Equal(4.5, plan.WeightedDaysLeft, 6);
    }

    [Fact]
    public void Last_partial_day_before_reset_hour_is_active_and_spends_all_remaining()
    {
        BudgetPlan plan = BudgetPlanner.Plan(Partial(15, consumed: 70), At(14, 10));

        Assert.Equal(PlanState.Active, plan.State);
        Assert.True(plan.TodayIsUsable);
        Assert.Equal(30, plan.TodayPercent, 6);
        Assert.Equal(30, plan.TodayAllowance);
        Assert.Equal(0, plan.LeftAfterToday, 6);
    }

    [Fact]
    public void Last_partial_day_after_reset_hour_is_ended_with_no_allowance()
    {
        BudgetPlan plan = BudgetPlanner.Plan(Partial(15, consumed: 70), At(14, 15, 30));

        Assert.Equal(PlanState.Ended, plan.State);
        Assert.Equal(0, plan.TodayAllowance);
        Assert.Equal(30, plan.LeftAfterToday, 6);
    }

    [Fact]
    public void Reset_hour_15_day_flips_at_midnight()
    {
        BudgetPlan plan = BudgetPlanner.Plan(Partial(15), new DateTime(2026, 10, 14, 0, 30, 0));

        Assert.Equal(new DateOnly(2026, 10, 14), plan.Today);
        Assert.Equal(PlanState.Active, plan.State);
    }

    [Fact]
    public void Reset_hour_4_keeps_previous_day_at_2am_on_reset_day()
    {
        BudgetPlan plan = BudgetPlanner.Plan(Partial(4), At(14, 2));

        Assert.Equal(new DateOnly(2026, 10, 13), plan.Today);
    }
}

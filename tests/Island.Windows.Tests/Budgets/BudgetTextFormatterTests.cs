using Island.App.Budgets;
using Island.Core.Budgets;

namespace Island.Windows.Tests.Budgets;

public sealed class BudgetTextFormatterTests
{
    private static Budget MakeBudget(int resetHour = 4, params DateOnly[] excluded) => new()
    {
        StartDate = new DateOnly(2026, 10, 9),
        EndDate = new DateOnly(2026, 10, 14),
        ResetHour = resetHour,
        TotalPercent = 100,
        Mode = EntryMode.Consumed,
        EnteredValue = 26,
        ExcludedDays = [.. excluded],
    };

    [Fact]
    public void Remaining_shows_trajectory_and_Hero_shows_per_day_when_today_is_usable()
    {
        Budget budget = MakeBudget(4, new DateOnly(2026, 10, 11));
        BudgetPlan plan = BudgetPlanner.Plan(budget, new DateTime(2026, 10, 9, 10, 0, 0));

        Assert.Equal("74% → 56%", BudgetTextFormatter.Remaining(plan));
        Assert.Equal("18,5% por dia", BudgetTextFormatter.Hero(plan));
        Assert.Equal("18%", BudgetTextFormatter.Allowance(plan));
    }

    [Fact]
    public void Remaining_is_plain_when_today_is_excluded()
    {
        Budget budget = MakeBudget(4, new DateOnly(2026, 10, 11));
        BudgetPlan plan = BudgetPlanner.Plan(budget, new DateTime(2026, 10, 11, 10, 0, 0));

        Assert.Equal("74%", BudgetTextFormatter.Remaining(plan));
    }

    [Fact]
    public void Reset_day_with_share_shows_percent_and_reset_hour()
    {
        Budget budget = MakeBudget(15);
        BudgetPlan plan = BudgetPlanner.Plan(budget, new DateTime(2026, 10, 10, 10, 0, 0));
        DayRow reset = plan.Days.Single(r => r.Status == DayStatus.Reset);

        string text = BudgetTextFormatter.DayValue(reset, 15);

        Assert.Contains("reinicia às 15h", text);
        string[] parts = text.Split(" · ");
        Assert.Equal(2, parts.Length);
        Assert.EndsWith("%", parts[0]);
    }

    [Fact]
    public void Reset_day_without_share_keeps_plain_reset_text()
    {
        Budget budget = MakeBudget(4);
        BudgetPlan plan = BudgetPlanner.Plan(budget, new DateTime(2026, 10, 9, 10, 0, 0));
        DayRow reset = plan.Days.Single(r => r.Status == DayStatus.Reset);

        Assert.Equal("Reinicia às 4h", BudgetTextFormatter.DayValue(reset, 4));
    }
}

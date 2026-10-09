

namespace Island.Core.Budgets;

public static class BudgetPlanner
{
    public static BudgetPlan Plan(Budget budget, DateTime now)
    {
        DateOnly today = DateOnly.FromDateTime(now.AddHours(-budget.ResetHour));
        var slots = ClassifyDays(budget, today);
        int usable = CountUsable(budget, slots, today);
        double remaining = budget.Remaining;
        double perDay = usable == 0 ? 0 : remaining / usable;
        PlanState state = GetState(budget, today);
        bool todayIsUsable = slots.Any(s => s.Date == today && s.Status == DayStatus.Available);
        int allowance = todayIsUsable && state == PlanState.Active ? ToAllowance(perDay) : 0;

        var days = slots
            .Select(s => new DayRow(s.Date, s.Status,
                s.Status == DayStatus.Available ? perDay : 0, s.Date == today))
            .ToList();

        return new BudgetPlan(days, state, today, remaining, budget.TotalPercent,
            usable, perDay, todayIsUsable, allowance, remaining - allowance);
    }

    private static List<(DateOnly Date, DayStatus Status)> ClassifyDays(Budget budget, DateOnly today)
    {
        DateOnly last = budget.EndDate > budget.StartDate ? budget.EndDate : budget.StartDate;
        int span = last.DayNumber - budget.StartDate.DayNumber;
        var result = new List<(DateOnly Date, DayStatus Status)>(span + 1);
        for (int i = 0; i <= span; i++)
        {
            DateOnly date = budget.StartDate.AddDays(i);
            result.Add((date, ClassifyDay(budget, date, today)));
        }
        return result;
    }

    private static DayStatus ClassifyDay(Budget budget, DateOnly date, DateOnly today)
    {
        if (date == budget.EndDate) return DayStatus.Reset;
        if (budget.ExcludedDays.Contains(date)) return DayStatus.Excluded;
        return date < today ? DayStatus.Past : DayStatus.Available;
    }

    private static int CountUsable(Budget budget, List<(DateOnly Date, DayStatus Status)> slots, DateOnly today)
    {
        if (budget.EndDate <= budget.StartDate) return 0;
        return slots.Count(s => s.Status == DayStatus.Available && s.Date >= today);
    }

    private static PlanState GetState(Budget budget, DateOnly today)
    {
        if (today < budget.StartDate) return PlanState.NotStarted;
        return today >= budget.EndDate ? PlanState.Ended : PlanState.Active;
    }

    private static int ToAllowance(double perDay)
    {
        double floored = Math.Floor(perDay + 1e-9);
        return (int)Math.Min(floored, int.MaxValue);
    }
}

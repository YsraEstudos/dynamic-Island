using Island.Core.Budgets;

namespace Island.App.Budgets;

/// <summary>Edições puras de Budget (retornam um novo registro; nunca mutam).</summary>
public static class BudgetEditing
{
    public static Budget WithStart(Budget budget, DateOnly start)
    {
        DateOnly end = budget.EndDate < start ? start.AddDays(1) : budget.EndDate;
        return budget with { StartDate = start, EndDate = end };
    }

    public static Budget WithEnd(Budget budget, DateOnly end)
    {
        DateOnly fixedEnd = end < budget.StartDate ? budget.StartDate.AddDays(1) : end;
        return budget with { EndDate = fixedEnd };
    }

    public static Budget WithResetHour(Budget budget, int hour) =>
        budget with { ResetHour = Math.Clamp(hour, 0, 23) };

    public static Budget ToggleExcluded(Budget budget, DateOnly date)
    {
        List<DateOnly> days = budget.ExcludedDays.Contains(date)
            ? budget.ExcludedDays.Where(d => d != date).ToList()
            : budget.ExcludedDays.Append(date).ToList();
        days.Sort();
        return budget with { ExcludedDays = days };
    }
}

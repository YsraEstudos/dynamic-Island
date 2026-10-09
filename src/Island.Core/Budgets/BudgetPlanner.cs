namespace Island.Core.Budgets;

public static class BudgetPlanner
{
    /// <summary>Reinício a partir desta hora: o dia de reinício conta como meio dia.</summary>
    public const int PartialResetDayFromHour = 9;

    /// <summary>Peso do dia de reinício parcial (meio dia).</summary>
    public const double PartialResetDayWeight = 0.5;

    public static BudgetPlan Plan(Budget budget, DateTime now)
    {
        DateOnly today = GetToday(budget, now);
        var slots = ClassifyDays(budget, today);
        int usable = CountUsable(budget, slots, today);
        double weighted = usable + (HasPartialResetDay(budget, today, now) ? PartialResetDayWeight : 0);
        double remaining = budget.Remaining;
        double perDay = weighted == 0 ? 0 : remaining / weighted;
        PlanState state = GetState(budget, today, now);
        bool todayAvailable = slots.Any(s => s.Date == today && s.Status == DayStatus.Available);
        bool todayPartial = state == PlanState.Active && IsPartialResetToday(budget, today, now);
        bool todayIsUsable = todayAvailable || todayPartial;
        double todayPercent = todayAvailable ? perDay : todayPartial ? perDay * PartialResetDayWeight : 0;
        int allowance = todayIsUsable && state == PlanState.Active ? ToAllowance(todayPercent) : 0;

        var days = slots
            .Select(s => new DayRow(s.Date, s.Status, RowPercent(budget, s.Status, perDay), s.Date == today))
            .ToList();

        return new BudgetPlan(days, state, today, remaining, budget.TotalPercent,
            usable, perDay, todayIsUsable, allowance, remaining - allowance, weighted, todayPercent);
    }

    private static DateOnly GetToday(Budget budget, DateTime now)
    {
        int shift = budget.ResetHour < PartialResetDayFromHour ? budget.ResetHour : 0;
        return DateOnly.FromDateTime(now.AddHours(-shift));
    }

    private static bool IsPartialReset(Budget budget) => budget.ResetHour >= PartialResetDayFromHour;

    private static TimeSpan ResetTime(Budget budget) => TimeSpan.FromHours(budget.ResetHour);

    private static bool HasPartialResetDay(Budget budget, DateOnly today, DateTime now) =>
        IsPartialReset(budget) && budget.EndDate > budget.StartDate && !HasEnded(budget, today, now);

    private static bool IsPartialResetToday(Budget budget, DateOnly today, DateTime now) =>
        today == budget.EndDate && HasPartialResetDay(budget, today, now);

    private static double RowPercent(Budget budget, DayStatus status, double perDay) => status switch
    {
        DayStatus.Available => perDay,
        DayStatus.Reset => IsPartialReset(budget) ? perDay * PartialResetDayWeight : 0,
        _ => 0,
    };

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

    private static PlanState GetState(Budget budget, DateOnly today, DateTime now)
    {
        if (today < budget.StartDate) return PlanState.NotStarted;
        return HasEnded(budget, today, now) ? PlanState.Ended : PlanState.Active;
    }

    private static bool HasEnded(Budget budget, DateOnly today, DateTime now)
    {
        if (!IsPartialReset(budget)) return today >= budget.EndDate;
        return today > budget.EndDate || (today == budget.EndDate && now.TimeOfDay >= ResetTime(budget));
    }

    private static int ToAllowance(double perDay)
    {
        double floored = Math.Floor(perDay + 1e-9);
        return (int)Math.Min(floored, int.MaxValue);
    }
}

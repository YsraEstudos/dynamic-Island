using System.Globalization;
using Island.Core.Budgets;

namespace Island.App.Budgets;

/// <summary>Textos pt-BR calculados a partir do plano (sem estado).</summary>
public static class BudgetTextFormatter
{
    private static readonly CultureInfo Pt = new("pt-BR");

    public static string DayLabel(DateOnly date)
    {
        string text = date.ToString("dddd (dd/MM)", Pt);
        return char.ToUpper(text[0], Pt) + text[1..];
    }

    public static string DayValue(DayRow row, int resetHour) => row.Status switch
    {
        DayStatus.Available => PercentFormatter.Format(row.Percent),
        DayStatus.Excluded => "Não usa",
        DayStatus.Reset => row.Percent > 0
            ? $"{PercentFormatter.Format(row.Percent)} · reinicia às {resetHour}h"
            : $"Reinicia às {resetHour}h",
        _ => "—",
    };

    public static string EntryLabel(EntryMode mode) =>
        mode == EntryMode.Consumed ? "Quanto você já consumiu" : "Quanto ainda resta";

    public static string Hero(BudgetPlan plan)
    {
        if (plan.WeightedDaysLeft == 0) return "—";
        double value = plan.TodayIsUsable ? plan.TodayPercent : plan.PerDayPercent;
        string number = PercentFormatter.Format(value);
        return HasPartialResetToday(plan) ? number + " hoje" : number + " por dia";
    }

    public static string Allowance(BudgetPlan plan) => PercentFormatter.Format(plan.TodayAllowance);

    public static string LeftAfterToday(BudgetPlan plan) => PercentFormatter.Format(plan.LeftAfterToday);

    public static string Remaining(BudgetPlan plan)
    {
        string now = PercentFormatter.Format(plan.RemainingPercent);
        bool showsTrajectory = plan.State == PlanState.Active && plan.TodayIsUsable && plan.TodayAllowance > 0;
        return showsTrajectory ? $"{now} → {PercentFormatter.Format(plan.LeftAfterToday)}" : now;
    }

    private static bool HasPartialResetToday(BudgetPlan plan) =>
        plan.Days.Any(r => r.IsToday && r.Status == DayStatus.Reset);

    public static double Progress(BudgetPlan plan) =>
        plan.TotalPercent > 0 ? Math.Clamp(plan.PerDayPercent / plan.TotalPercent, 0, 1) : 0;

    public static string Status(Budget budget, BudgetPlan plan)
    {
        if (plan.State == PlanState.NotStarted)
        {
            return "Começa em " + budget.StartDate.ToString("dd/MM", Pt);
        }

        if (plan.State == PlanState.Ended)
        {
            return "Período encerrado";
        }

        bool todayExcluded = plan.Days.Any(r => r.IsToday && r.Status == DayStatus.Excluded);
        return todayExcluded ? "Hoje você marcou como dia sem uso" : string.Empty;
    }
}

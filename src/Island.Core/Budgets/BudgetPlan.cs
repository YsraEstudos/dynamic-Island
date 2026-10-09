namespace Island.Core.Budgets;

/// <summary>Resultado do cálculo diário de um orçamento em um determinado instante.</summary>
public sealed record BudgetPlan(
    IReadOnlyList<DayRow> Days,
    PlanState State,
    DateOnly Today,
    double RemainingPercent,
    double TotalPercent,
    int UsableDaysLeft,
    double PerDayPercent,
    bool TodayIsUsable,
    int TodayAllowance,
    double LeftAfterToday,
    double WeightedDaysLeft,
    double TodayPercent);

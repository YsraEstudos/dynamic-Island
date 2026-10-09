namespace Island.Core.Budgets;

public sealed record DayRow(DateOnly Date, DayStatus Status, double Percent, bool IsToday);

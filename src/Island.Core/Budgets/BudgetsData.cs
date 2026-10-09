namespace Island.Core.Budgets;

/// <summary>What is persisted: every budget plus which one the widget shows.</summary>
public sealed record BudgetsData(List<Budget> Budgets, Guid? SelectedId);

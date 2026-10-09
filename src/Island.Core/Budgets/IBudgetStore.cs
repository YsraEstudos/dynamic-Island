namespace Island.Core.Budgets;

public interface IBudgetStore
{
    /// <summary>Never throws: a missing or corrupt file yields empty data.</summary>
    BudgetsData Load();

    void Save(BudgetsData data);
}

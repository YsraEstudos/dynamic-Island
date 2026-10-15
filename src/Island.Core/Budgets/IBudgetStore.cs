namespace Island.Core.Budgets;

public interface IBudgetStore
{
    /// <summary>Never throws: a missing or corrupt file yields empty data.</summary>
    BudgetsData Load();

    /// <summary>A failed read disables editing until the data can be loaded again.</summary>
    string? LoadError => null;

    void Save(BudgetsData data);
}

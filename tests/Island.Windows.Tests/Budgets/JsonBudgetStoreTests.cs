using Island.Core.Budgets;
using Island.Windows.Budgets;

namespace Island.Windows.Tests.Budgets;

public sealed class JsonBudgetStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "PorcentagemCoreTests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "budgets.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Save_then_Load_roundtrips_budgets_and_selection()
    {
        var budget = new Budget
        {
            Name = "Claude",
            StartDate = new DateOnly(2026, 10, 9),
            EndDate = new DateOnly(2026, 10, 14),
            ResetHour = 6,
            TotalPercent = 74,
            Mode = EntryMode.Remaining,
            EnteredValue = 24,
            ExcludedDays = [new DateOnly(2026, 10, 11)],
        };
        var store = new JsonBudgetStore(_dir);

        store.Save(new BudgetsData([budget], budget.Id));
        BudgetsData loaded = new JsonBudgetStore(_dir).Load();

        Budget restored = Assert.Single(loaded.Budgets);
        Assert.Equal(budget.Id, restored.Id);
        Assert.Equal("Claude", restored.Name);
        Assert.Equal(budget.StartDate, restored.StartDate);
        Assert.Equal(budget.EndDate, restored.EndDate);
        Assert.Equal(6, restored.ResetHour);
        Assert.Equal(74, restored.TotalPercent, 6);
        Assert.Equal(EntryMode.Remaining, restored.Mode);
        Assert.Equal(24, restored.EnteredValue, 6);
        Assert.Equal(budget.ExcludedDays, restored.ExcludedDays);
        Assert.Equal<Guid?>(budget.Id, loaded.SelectedId);
    }

    [Fact]
    public void Save_writes_enum_as_text_with_camelCase_names()
    {
        var store = new JsonBudgetStore(_dir);

        store.Save(new BudgetsData([new Budget { Mode = EntryMode.Remaining }], null));

        string json = File.ReadAllText(FilePath);
        Assert.Contains("\"mode\": \"Remaining\"", json);
    }

    [Fact]
    public void Load_missing_file_returns_empty_data()
    {
        BudgetsData data = new JsonBudgetStore(_dir).Load();

        Assert.Empty(data.Budgets);
        Assert.Null(data.SelectedId);
    }

    [Fact]
    public void Load_corrupted_json_returns_empty_data()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ isto nao e json");

        BudgetsData data = new JsonBudgetStore(_dir).Load();

        Assert.Empty(data.Budgets);
        Assert.Null(data.SelectedId);
    }

    [Fact]
    public void Save_creates_missing_folder_and_leaves_no_tmp_file()
    {
        string folder = Path.Combine(_dir, "nested");
        string nested = Path.Combine(folder, "budgets.json");

        new JsonBudgetStore(folder).Save(new BudgetsData(new List<Budget>(), null));

        Assert.True(File.Exists(nested));
        Assert.False(File.Exists(nested + ".tmp"));
    }
}

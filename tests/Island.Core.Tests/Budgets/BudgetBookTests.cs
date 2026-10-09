using Island.Core.Budgets;

namespace Island.Core.Tests.Budgets;

public sealed class BudgetBookTests
{
    private sealed class MemoryStore(BudgetsData? initial = null) : IBudgetStore
    {
        public BudgetsData? Saved { get; private set; }
        public int SaveCount { get; private set; }

        public BudgetsData Load() => initial ?? new BudgetsData(new List<Budget>(), null);

        public void Save(BudgetsData data)
        {
            Saved = data;
            SaveCount++;
        }
    }

    private static readonly DateTime Now = new(2026, 10, 9, 10, 0, 0);

    private static BudgetBook NewBook(MemoryStore store) => new(store, () => Now);

    [Fact]
    public void Empty_store_seeds_one_budget_without_saving()
    {
        var store = new MemoryStore();
        var book = NewBook(store);

        Assert.Single(book.Budgets);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(new DateOnly(2026, 10, 9), book.Selected!.StartDate);
    }

    [Fact]
    public void Add_selects_new_budget_saves_and_raises_Changed()
    {
        var store = new MemoryStore();
        var book = NewBook(store);
        int changed = 0;
        book.Changed += () => changed++;

        Budget added = book.Add();

        Assert.Equal(2, book.Budgets.Count);
        Assert.Equal(added.Id, book.Selected!.Id);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Remove_selects_the_previous_budget()
    {
        var book = NewBook(new MemoryStore());
        Budget first = book.Budgets[0];
        Budget second = book.Add();

        book.Remove(second.Id);

        Assert.Equal(first.Id, book.Selected!.Id);
    }

    [Fact]
    public void Cycle_wraps_around_in_both_directions()
    {
        var book = NewBook(new MemoryStore());
        Budget first = book.Budgets[0];
        Budget second = book.Add();

        book.Cycle(1);
        Assert.Equal(first.Id, book.Selected!.Id);
        book.Cycle(-1);
        Assert.Equal(second.Id, book.Selected!.Id);
    }

    [Fact]
    public void Update_with_identical_content_is_a_noop()
    {
        var store = new MemoryStore();
        var book = NewBook(store);
        Budget budget = book.Budgets[0];

        book.Update(budget with { ExcludedDays = new List<DateOnly>() });

        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void AddUsage_in_Consumed_mode_raises_the_entered_value()
    {
        var book = NewBook(new MemoryStore());
        Budget budget = book.Budgets[0];
        book.Update(budget with { EnteredValue = 26 });

        book.AddUsage(budget.Id, 5);

        Assert.Equal(31, book.Selected!.EnteredValue);
        Assert.Equal(69, book.Selected.Remaining);
    }

    [Fact]
    public void AddUsage_in_Remaining_mode_lowers_the_entered_value_and_clamps_at_zero()
    {
        var book = NewBook(new MemoryStore());
        Budget budget = book.Budgets[0];
        book.Update(budget with { Mode = EntryMode.Remaining, EnteredValue = 3 });

        book.AddUsage(budget.Id, 5);

        Assert.Equal(0, book.Selected!.EnteredValue);
        Assert.Equal(0, book.Selected.Remaining);
    }
}

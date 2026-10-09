namespace Island.Core.Budgets;

/// <summary>
/// The user's budgets (one per AI), shared by the shelf widget and the editor window.
/// Every change is saved right away; the file is tiny and edits are human-paced.
/// </summary>
public sealed class BudgetBook
{
    private const int DefaultResetHour = 4;
    private const int DefaultPeriodDays = 7;

    private readonly IBudgetStore _store;
    private readonly Func<DateTime> _clock;
    private readonly List<Budget> _budgets;
    private Guid? _selectedId;

    public BudgetBook(IBudgetStore store, Func<DateTime>? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? (() => DateTime.Now);
        BudgetsData data = store.Load();
        _budgets = data.Budgets.ToList();
        _selectedId = data.SelectedId;
        if (_budgets.Count == 0) _budgets.Add(NewBudget("Claude")); // memory only until the first change
    }

    public event Action? Changed;

    public IReadOnlyList<Budget> Budgets => _budgets;

    public DateTime Now => _clock();

    public Budget? Selected => _budgets.FirstOrDefault(b => b.Id == _selectedId) ?? _budgets.FirstOrDefault();

    public BudgetPlan PlanOf(Budget budget) => BudgetPlanner.Plan(budget, _clock());

    public Budget Add()
    {
        Budget budget = NewBudget("Nova IA");
        _budgets.Add(budget);
        _selectedId = budget.Id;
        Commit();
        return budget;
    }

    public void Remove(Guid id)
    {
        int index = _budgets.FindIndex(b => b.Id == id);
        if (index < 0) return;

        _budgets.RemoveAt(index);
        if (_selectedId == id) _selectedId = _budgets.Count == 0 ? null : _budgets[Math.Max(0, index - 1)].Id;
        Commit();
    }

    /// <summary>Replaces the budget with the same Id. No-op when nothing changed.</summary>
    public void Update(Budget budget)
    {
        int index = _budgets.FindIndex(b => b.Id == budget.Id);
        if (index < 0 || SameContent(_budgets[index], budget)) return;

        _budgets[index] = budget;
        Commit();
    }

    public void Select(Guid? id)
    {
        if (_selectedId == id) return;

        _selectedId = id;
        Commit();
    }

    /// <summary>Selects the next (+1) or previous (-1) budget, wrapping around.</summary>
    public void Cycle(int direction)
    {
        if (_budgets.Count < 2) return;

        int current = Math.Max(0, _budgets.FindIndex(b => b.Id == Selected?.Id));
        int next = (current + direction + _budgets.Count) % _budgets.Count;
        Select(_budgets[next].Id);
    }

    /// <summary>Records <paramref name="percent"/> more consumption, whichever way the value is entered.</summary>
    public void AddUsage(Guid id, double percent)
    {
        Budget? budget = _budgets.FirstOrDefault(b => b.Id == id);
        if (budget is null) return;

        double delta = budget.Mode == EntryMode.Consumed ? percent : -percent;
        double entered = Math.Clamp(budget.EnteredValue + delta, 0, budget.TotalPercent);
        Update(budget with { EnteredValue = entered });
    }

    private Budget NewBudget(string name)
    {
        DateOnly start = DateOnly.FromDateTime(_clock().AddHours(-DefaultResetHour));
        return new Budget
        {
            Name = name,
            StartDate = start,
            EndDate = start.AddDays(DefaultPeriodDays),
            ResetHour = DefaultResetHour,
        };
    }

    // Records compare ExcludedDays by reference, so compare it by value here.
    private static bool SameContent(Budget a, Budget b)
    {
        List<DateOnly> shared = new();
        return a with { ExcludedDays = shared } == b with { ExcludedDays = shared }
            && a.ExcludedDays.SequenceEqual(b.ExcludedDays);
    }

    private void Commit()
    {
        try { _store.Save(new BudgetsData(_budgets.ToList(), _selectedId)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* next change retries */ }
        Changed?.Invoke();
    }
}

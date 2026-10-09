using System.Collections.ObjectModel;
using System.Windows.Input;
using Island.Core.Budgets;

namespace Island.App.Budgets;

/// <summary>Editor-window view model: one <see cref="BudgetViewModel"/> per budget of the shared <see cref="BudgetBook"/>.</summary>
public sealed class BudgetsViewModel : ObservableObject
{
    private readonly BudgetBook _book;
    private readonly RelayCommand _remove;
    private BudgetViewModel? _selected;
    private bool _pushing;

    public BudgetsViewModel(BudgetBook book)
    {
        _book = book;
        _remove = new RelayCommand(_ => Remove(), _ => Selected is not null);
        AddCommand = new RelayCommand(_ => _book.Add());
        _book.Changed += OnBookChanged;
        Sync();
    }

    public ObservableCollection<BudgetViewModel> Budgets { get; } = new();

    public BudgetViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value)) return;

            _remove.RaiseCanExecuteChanged();
            if (value is not null) _book.Select(value.Id);
        }
    }

    public bool HasBudgets => Budgets.Count > 0;

    public ICommand AddCommand { get; }

    public ICommand RemoveCommand => _remove;

    /// <summary>Recomputes every plan (the window was activated; the day may have turned).</summary>
    public void Refresh()
    {
        foreach (BudgetViewModel budget in Budgets) budget.Recalculate(_book.Now);
    }

    public void Detach() => _book.Changed -= OnBookChanged;

    private void Remove()
    {
        if (Selected is { } current) _book.Remove(current.Id);
    }

    private void OnBookChanged()
    {
        if (!_pushing) Sync();
    }

    private void OnBudgetEdited(BudgetViewModel vm)
    {
        _pushing = true;
        try { _book.Update(vm.Model); }
        finally { _pushing = false; }
    }

    private void Sync()
    {
        foreach (BudgetViewModel gone in Budgets.Where(vm => _book.Budgets.All(b => b.Id != vm.Id)).ToList())
        {
            Budgets.Remove(gone);
        }

        foreach (Budget budget in _book.Budgets)
        {
            BudgetViewModel? existing = Budgets.FirstOrDefault(vm => vm.Id == budget.Id);
            if (existing is null) Track(budget);
            else if (!ReferenceEquals(existing.Model, budget)) existing.Reload(budget);
        }

        Selected = Budgets.FirstOrDefault(vm => vm.Id == _book.Selected?.Id);
        Raise(nameof(HasBudgets));
    }

    private void Track(Budget budget)
    {
        var vm = new BudgetViewModel(budget, () => _book.Now);
        vm.Changed += () => OnBudgetEdited(vm);
        Budgets.Add(vm);
    }
}

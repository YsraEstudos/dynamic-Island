using Island.App.Budgets;

namespace Island.App.Budgets;

public sealed partial class BudgetViewModel
{
    private static readonly string[] DerivedNames =
    {
        nameof(EntryLabel), nameof(HeroText), nameof(TodayAllowanceText), nameof(LeftAfterTodayText),
        nameof(RemainingText), nameof(ProgressRatio), nameof(StatusMessage), nameof(HasStatusMessage),
    };

    // ---- calculados (somente leitura) ----

    public string EntryLabel => BudgetTextFormatter.EntryLabel(Model.Mode);

    public string HeroText => BudgetTextFormatter.Hero(_plan);

    public string TodayAllowanceText => BudgetTextFormatter.Allowance(_plan);

    public string LeftAfterTodayText => BudgetTextFormatter.LeftAfterToday(_plan);

    public string RemainingText => BudgetTextFormatter.Remaining(_plan);

    public double ProgressRatio => BudgetTextFormatter.Progress(_plan);

    public string StatusMessage => BudgetTextFormatter.Status(Model, _plan);

    public bool HasStatusMessage => StatusMessage.Length > 0;

    private void RaiseDerived()
    {
        foreach (string name in DerivedNames)
        {
            Raise(name);
        }
    }
}

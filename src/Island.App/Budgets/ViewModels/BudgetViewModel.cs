using System.Collections.ObjectModel;
using Island.App.Budgets;
using Island.Core.Budgets;

namespace Island.App.Budgets;

/// <summary>Estado e edições do orçamento; os textos calculados ficam em BudgetViewModel.Display.cs.</summary>
public sealed partial class BudgetViewModel : ObservableObject
{
    private static readonly string[] EditableNames =
    {
        nameof(Name), nameof(StartDate), nameof(EndDate), nameof(ResetHour), nameof(TotalText),
        nameof(EnteredText), nameof(IsConsumedMode), nameof(IsRemainingMode),
    };

    private readonly Func<DateTime> _clock;
    private BudgetPlan _plan;

    public BudgetViewModel(Budget model, Func<DateTime> clock)
    {
        _clock = clock;
        Model = model;
        _plan = BudgetPlanner.Plan(model, clock());
        DayRowSync.Sync(Days, _plan.Days, model.ResetHour, ToggleDay);
    }

    public event Action? Changed;

    public Guid Id => Model.Id;

    public Budget Model { get; private set; }

    public ObservableCollection<DayRowViewModel> Days { get; } = new();

    // ---- editáveis ----

    public string Name
    {
        get => Model.Name;
        set => Apply(Model with { Name = value ?? string.Empty }, nameof(Name));
    }

    public DateTime StartDate
    {
        get => Model.StartDate.ToDateTime(TimeOnly.MinValue);
        set => Apply(BudgetEditing.WithStart(Model, DateOnly.FromDateTime(value)), nameof(StartDate));
    }

    public DateTime EndDate
    {
        get => Model.EndDate.ToDateTime(TimeOnly.MinValue);
        set => Apply(BudgetEditing.WithEnd(Model, DateOnly.FromDateTime(value)), nameof(EndDate));
    }

    public int ResetHour
    {
        get => Model.ResetHour;
        set => Apply(BudgetEditing.WithResetHour(Model, value), null);
    }

    public string TotalText
    {
        get => PercentFormatter.Format(Model.TotalPercent);
        set => ApplyPercent(value, v => Model with { TotalPercent = v }, nameof(TotalText));
    }

    public string EnteredText
    {
        get => PercentFormatter.Format(Model.EnteredValue);
        set => ApplyPercent(value, v => Model with { EnteredValue = v }, nameof(EnteredText));
    }

    public bool IsConsumedMode
    {
        get => Model.Mode == EntryMode.Consumed;
        set { if (value) Apply(Model with { Mode = EntryMode.Consumed }, nameof(IsConsumedMode)); }
    }

    public bool IsRemainingMode
    {
        get => Model.Mode == EntryMode.Remaining;
        set { if (value) Apply(Model with { Mode = EntryMode.Remaining }, nameof(IsRemainingMode)); }
    }

    /// <summary>Adopts a budget changed elsewhere (the shelf widget) without raising <see cref="Changed"/>.</summary>
    public void Reload(Budget model)
    {
        Model = model;
        Recalculate(_clock());
        foreach (string name in EditableNames)
        {
            Raise(name);
        }
    }

    public void Recalculate(DateTime now)
    {
        _plan = BudgetPlanner.Plan(Model, now);
        DayRowSync.Sync(Days, _plan.Days, Model.ResetHour, ToggleDay);
        RaiseDerived();
    }

    /// <summary>Texto inválido é ignorado: a propriedade é renotificada e volta ao último valor válido.</summary>
    private void ApplyPercent(string text, Func<double, Budget> change, string name)
    {
        if (PercentFormatter.TryParse(text, out double value))
        {
            Apply(change(value), name);
        }
        else
        {
            Raise(name);
        }
    }

    /// <summary>Notifica tudo exceto o campo editado (para não reformatar o texto que o usuário digita).</summary>
    private void Apply(Budget next, string? edited)
    {
        if (next.Equals(Model))
        {
            return;
        }

        Model = next;
        Recalculate(_clock());
        foreach (string name in EditableNames)
        {
            if (name != edited)
            {
                Raise(name);
            }
        }

        Changed?.Invoke();
    }

    private void ToggleDay(DateOnly date)
    {
        DayRow? row = _plan.Days.FirstOrDefault(r => r.Date == date);
        if (row is null || row.Status is DayStatus.Past or DayStatus.Reset)
        {
            return;
        }

        Apply(BudgetEditing.ToggleExcluded(Model, date), null);
    }
}

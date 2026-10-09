using System.Windows.Input;
using Island.App.Budgets;
using Island.Core.Budgets;

namespace Island.App.Budgets;

public sealed class DayRowViewModel : ObservableObject
{
    private static readonly string[] ChangeableNames =
    {
        nameof(ValueText), nameof(IsToday), nameof(IsPast), nameof(IsExcluded), nameof(CanToggle),
    };

    private readonly RelayCommand _toggle;
    private DayRow _row;
    private int _resetHour;

    public DayRowViewModel(DayRow row, int resetHour, Action<DateOnly> onToggle)
    {
        _row = row;
        _resetHour = resetHour;
        Date = row.Date;
        Label = BudgetTextFormatter.DayLabel(row.Date);
        _toggle = new RelayCommand(_ => onToggle(Date), _ => CanToggle);
    }

    public DateOnly Date { get; }

    public string Label { get; }

    public string ValueText => BudgetTextFormatter.DayValue(_row, _resetHour);

    public bool IsToday => _row.IsToday;

    public bool IsPast => _row.Status == DayStatus.Past;

    public bool IsExcluded => _row.Status == DayStatus.Excluded;

    public bool CanToggle => _row.Status is DayStatus.Available or DayStatus.Excluded;

    public ICommand ToggleCommand => _toggle;

    /// <summary>Atualiza a linha in-place (a data não muda para o mesmo item).</summary>
    public void Update(DayRow row, int resetHour)
    {
        if (row == _row && resetHour == _resetHour)
        {
            return;
        }

        _row = row;
        _resetHour = resetHour;
        foreach (string name in ChangeableNames)
        {
            Raise(name);
        }

        _toggle.RaiseCanExecuteChanged();
    }
}

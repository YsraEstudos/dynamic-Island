using System.Collections.ObjectModel;
using Island.Core.Budgets;

namespace Island.App.Budgets;

/// <summary>Atualiza a coleção de dias reaproveitando cada DayRowViewModel pela data.</summary>
public static class DayRowSync
{
    public static void Sync(
        ObservableCollection<DayRowViewModel> days,
        IReadOnlyList<DayRow> rows,
        int resetHour,
        Action<DateOnly> onToggle)
    {
        Dictionary<DateOnly, DayRowViewModel> existing = days.ToDictionary(d => d.Date);
        for (int i = 0; i < rows.Count; i++)
        {
            DayRow row = rows[i];
            DayRowViewModel vm = existing.TryGetValue(row.Date, out DayRowViewModel? found)
                ? found
                : new DayRowViewModel(row, resetHour, onToggle);

            if (i < days.Count)
            {
                if (!ReferenceEquals(days[i], vm))
                {
                    days[i] = vm;
                }
            }
            else
            {
                days.Add(vm);
            }

            vm.Update(row, resetHour);
        }

        while (days.Count > rows.Count)
        {
            days.RemoveAt(days.Count - 1);
        }
    }
}

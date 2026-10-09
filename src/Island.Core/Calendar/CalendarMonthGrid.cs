namespace Island.Core.Calendar;

/// <summary>Builds a six-week month grid without leaving the supported date range.</summary>
public static class CalendarMonthGrid
{
    public const int CellCount = 42;

    public static IReadOnlyList<DateOnly?> GetDates(DateOnly month, DayOfWeek firstDayOfWeek)
    {
        if (firstDayOfWeek is < DayOfWeek.Sunday or > DayOfWeek.Saturday)
            throw new ArgumentOutOfRangeException(nameof(firstDayOfWeek));

        var first = new DateOnly(month.Year, month.Month, 1);
        int lead = ((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        var dates = new DateOnly?[CellCount];
        for (int i = 0; i < dates.Length; i++)
        {
            int dayNumber = first.DayNumber - lead + i;
            if (dayNumber >= DateOnly.MinValue.DayNumber && dayNumber <= DateOnly.MaxValue.DayNumber)
                dates[i] = DateOnly.FromDayNumber(dayNumber);
        }
        return dates;
    }

    public static bool TryMoveMonth(DateOnly month, int offset, out DateOnly next)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        try
        {
            next = first.AddMonths(offset);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            next = first;
            return false;
        }
    }
}

using Island.Core.Calendar;

namespace Island.Core.Tests;

public sealed class CalendarMonthGridTests
{
    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(9999, 12, 0)]
    [InlineData(9999, 12, 1)]
    public void Boundary_months_keep_all_month_dates_and_pad_unsupported_days(int year, int month, int firstDay)
    {
        var dates = CalendarMonthGrid.GetDates(new DateOnly(year, month, 1), (DayOfWeek)firstDay);

        Assert.Equal(42, dates.Count);
        var inMonth = dates.Where(date => date is { } day && day.Year == year && day.Month == month).ToArray();
        Assert.Equal(DateTime.DaysInMonth(year, month), inMonth.Length);
        Assert.Equal(new DateOnly(year, month, 1), inMonth[0]);
        Assert.Equal(new DateOnly(year, month, DateTime.DaysInMonth(year, month)), inMonth[^1]);
    }

    [Fact]
    public void Sunday_grid_places_October_2026_first_day_in_Thursday_column()
    {
        var dates = CalendarMonthGrid.GetDates(new DateOnly(2026, 10, 9), DayOfWeek.Sunday);

        Assert.Equal(new DateOnly(2026, 9, 27), dates[0]);
        Assert.Equal(new DateOnly(2026, 10, 1), dates[4]);
        Assert.Equal(new DateOnly(2026, 11, 7), dates[^1]);
    }

    [Theory]
    [InlineData(1, 2, -1, 1, 1)]
    [InlineData(9999, 11, 1, 9999, 12)]
    [InlineData(2026, 1, -1, 2025, 12)]
    [InlineData(2026, 12, 1, 2027, 1)]
    public void Navigation_reaches_valid_months_in_boundary_years(int year, int month, int offset, int nextYear, int nextMonth)
    {
        Assert.True(CalendarMonthGrid.TryMoveMonth(new DateOnly(year, month, 20), offset, out var next));
        Assert.Equal(new DateOnly(nextYear, nextMonth, 1), next);
    }

    [Theory]
    [InlineData(1, 1, -1)]
    [InlineData(9999, 12, 1)]
    public void Navigation_stops_only_at_the_first_and_last_supported_month(int year, int month, int offset)
    {
        var monthStart = new DateOnly(year, month, 1);
        Assert.False(CalendarMonthGrid.TryMoveMonth(monthStart, offset, out var next));
        Assert.Equal(monthStart, next);
    }
}

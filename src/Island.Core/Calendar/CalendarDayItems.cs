namespace Island.Core.Calendar;

/// <summary>Calendar items that belong to one local calendar date.</summary>
public sealed record CalendarDayItems(
    IReadOnlyList<CalendarTask> Tasks,
    IReadOnlyList<CalendarBirthday> Birthdays,
    IReadOnlyList<CalendarEvent> Events);

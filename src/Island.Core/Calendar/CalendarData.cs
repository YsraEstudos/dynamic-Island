namespace Island.Core.Calendar;

/// <summary>A persisted snapshot of calendar items. Lists are replaced as a unit when the agenda changes.</summary>
public sealed record CalendarData
{
    public IReadOnlyList<CalendarTask> Tasks { get; init; } = Array.Empty<CalendarTask>();
    public IReadOnlyList<CalendarEvent> Events { get; init; } = Array.Empty<CalendarEvent>();
    public IReadOnlyList<CalendarBirthday> Birthdays { get; init; } = Array.Empty<CalendarBirthday>();

    public static CalendarData Empty => new();
}

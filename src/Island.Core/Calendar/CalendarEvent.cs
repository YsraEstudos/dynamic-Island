namespace Island.Core.Calendar;

public sealed record CalendarEvent(Guid Id, string Title, DateOnly Date, TimeOnly? Time);

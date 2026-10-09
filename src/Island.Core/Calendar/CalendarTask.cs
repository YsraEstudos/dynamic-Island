namespace Island.Core.Calendar;

public sealed record CalendarTask(Guid Id, string Title, DateOnly DueDate, bool IsCompleted);

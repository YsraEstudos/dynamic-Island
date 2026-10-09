using Island.Core.Calendar;

namespace Island.Core.Abstractions;

public interface ICalendarStore
{
    CalendarData Load();
    void Save(CalendarData data);
}

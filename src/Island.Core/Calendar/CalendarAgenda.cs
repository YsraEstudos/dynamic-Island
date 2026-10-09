using Island.Core.Abstractions;

namespace Island.Core.Calendar;

/// <summary>Owns the live calendar snapshot and publishes it only after a successful store write.</summary>
public sealed class CalendarAgenda
{
    private readonly object _gate = new();
    private readonly ICalendarStore _store;
    private CalendarData _data;

    public CalendarAgenda(ICalendarStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _data = Normalize(store.Load());
    }

    public event Action? Changed;

    public bool HasPendingTasks
    {
        get
        {
            lock (_gate) return _data.Tasks.Any(task => !task.IsCompleted);
        }
    }

    public CalendarDayItems GetDay(DateOnly date)
    {
        lock (_gate)
        {
            return new CalendarDayItems(
                _data.Tasks.Where(task => task.DueDate == date)
                    .OrderBy(task => task.IsCompleted)
                    .ThenBy(task => task.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray(),
                _data.Birthdays.Where(birthday => birthday.Month == date.Month && birthday.Day == date.Day)
                    .OrderBy(birthday => birthday.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray(),
                _data.Events.Where(calendarEvent => calendarEvent.Date == date)
                    .OrderBy(calendarEvent => calendarEvent.Time.HasValue)
                    .ThenBy(calendarEvent => calendarEvent.Time)
                    .ThenBy(calendarEvent => calendarEvent.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray());
        }
    }

    public IReadOnlySet<DateOnly> GetMarkedDates(int year, int month)
    {
        _ = new DateOnly(year, month, 1);
        lock (_gate)
        {
            var marked = new HashSet<DateOnly>();
            foreach (CalendarTask task in _data.Tasks)
            {
                if (task.DueDate.Year == year && task.DueDate.Month == month) marked.Add(task.DueDate);
            }
            foreach (CalendarEvent calendarEvent in _data.Events)
            {
                if (calendarEvent.Date.Year == year && calendarEvent.Date.Month == month) marked.Add(calendarEvent.Date);
            }
            foreach (CalendarBirthday birthday in _data.Birthdays)
            {
                if (birthday.Month == month && birthday.Day <= DateTime.DaysInMonth(year, month))
                    marked.Add(new DateOnly(year, month, birthday.Day));
            }
            return marked;
        }
    }

    public CalendarEvent AddEvent(string title, DateOnly date, TimeOnly? time)
    {
        var created = new CalendarEvent(Guid.NewGuid(), NormalizeName(title, nameof(title)), date, time);
        Commit(data => data with { Events = data.Events.Append(created).ToArray() });
        return created;
    }

    public CalendarBirthday AddBirthday(string name, int month, int day)
    {
        string normalizedName = NormalizeName(name, nameof(name));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        if (day < 1 || day > DateTime.DaysInMonth(2000, month)) throw new ArgumentOutOfRangeException(nameof(day));

        var created = new CalendarBirthday(Guid.NewGuid(), normalizedName, month, day);
        Commit(data => data with { Birthdays = data.Birthdays.Append(created).ToArray() });
        return created;
    }

    public CalendarTask AddTask(string title, DateOnly dueDate)
    {
        var created = new CalendarTask(Guid.NewGuid(), NormalizeName(title, nameof(title)), dueDate, false);
        Commit(data => data with { Tasks = data.Tasks.Append(created).ToArray() });
        return created;
    }

    public bool SetTaskCompleted(Guid id, bool isCompleted)
    {
        bool found;
        bool changed;
        lock (_gate)
        {
            CalendarTask? task = _data.Tasks.FirstOrDefault(item => item.Id == id);
            if (task is null) return false;
            found = true;
            changed = task.IsCompleted != isCompleted;
            if (changed)
            {
                CalendarData next = _data with
                {
                    Tasks = _data.Tasks.Select(item => item.Id == id ? item with { IsCompleted = isCompleted } : item).ToArray(),
                };
                _store.Save(next);
                _data = next;
            }
        }

        if (changed) Changed?.Invoke();
        return found;
    }

    private void Commit(Func<CalendarData, CalendarData> createNext)
    {
        lock (_gate)
        {
            CalendarData next = createNext(_data);
            _store.Save(next);
            _data = next;
        }
        Changed?.Invoke();
    }

    private static CalendarData Normalize(CalendarData? data)
    {
        return new CalendarData
        {
            Tasks = (data?.Tasks ?? Array.Empty<CalendarTask>())
                .Where(task => task is not null && !string.IsNullOrWhiteSpace(task.Title))
                .Select(task => task with { Title = task.Title.Trim() })
                .ToArray(),
            Events = (data?.Events ?? Array.Empty<CalendarEvent>())
                .Where(calendarEvent => calendarEvent is not null && !string.IsNullOrWhiteSpace(calendarEvent.Title))
                .Select(calendarEvent => calendarEvent with { Title = calendarEvent.Title.Trim() })
                .ToArray(),
            Birthdays = (data?.Birthdays ?? Array.Empty<CalendarBirthday>())
                .Where(birthday => birthday is not null && !string.IsNullOrWhiteSpace(birthday.Name)
                    && IsValidBirthday(birthday.Month, birthday.Day))
                .Select(birthday => birthday with { Name = birthday.Name.Trim() })
                .ToArray(),
        };
    }

    private static bool IsValidBirthday(int month, int day)
    {
        try { _ = new DateOnly(2000, month, day); return true; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static string NormalizeName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

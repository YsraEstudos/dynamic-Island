using Island.Core.Calendar;
using Island.Windows.Calendar;

namespace Island.Windows.Tests;

public sealed class JsonCalendarStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "island-calendar-" + Guid.NewGuid().ToString("N"));
    private string PathToCalendar => Path.Combine(_directory, "calendar.json");

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Round_trip_preserves_tasks_events_and_recurring_birthdays()
    {
        var data = new CalendarData
        {
            Tasks = [new CalendarTask(Guid.NewGuid(), "Tarefa", new DateOnly(2026, 10, 9), true)],
            Events = [new CalendarEvent(Guid.NewGuid(), "Consulta", new DateOnly(2026, 10, 10), new TimeOnly(9, 30))],
            Birthdays = [new CalendarBirthday(Guid.NewGuid(), "Pessoa", 2, 29)],
        };
        var store = new JsonCalendarStore(_directory);

        store.Save(data);
        CalendarData loaded = store.Load();

        Assert.Equal(data.Tasks, loaded.Tasks);
        Assert.Equal(data.Events, loaded.Events);
        Assert.Equal(data.Birthdays, loaded.Birthdays);
    }

    [Fact]
    public void Repeated_corrupt_loads_preserve_every_recovery_copy()
    {
        Directory.CreateDirectory(_directory);
        string firstRecovery = PathToCalendar + ".bad";
        byte[] earlierCorruption = [0x7B, 0x20, 0x31, 0x20, 0x7D];
        File.WriteAllBytes(firstRecovery, earlierCorruption);
        File.WriteAllText(PathToCalendar, "{ broken first");

        Assert.Equal(CalendarData.Empty, new JsonCalendarStore(_directory).Load());

        File.WriteAllText(PathToCalendar, "{ broken second");
        Assert.Equal(CalendarData.Empty, new JsonCalendarStore(_directory).Load());

        Assert.Equal(earlierCorruption, File.ReadAllBytes(firstRecovery));
        Assert.Contains(Directory.GetFiles(_directory, "calendar.json.bad-*"), recovery =>
            File.ReadAllText(recovery) == "{ broken second");
        Assert.Contains(Directory.GetFiles(_directory, "calendar.json.bad-*"), recovery =>
            File.ReadAllText(recovery) == "{ broken first");
        Assert.False(File.Exists(PathToCalendar));
    }

    [Fact]
    public void Save_replaces_existing_file_and_removes_temporary_file()
    {
        var store = new JsonCalendarStore(_directory);
        var first = new CalendarData { Tasks = [new(Guid.NewGuid(), "Primeira", new DateOnly(2026, 10, 9), false)] };
        var second = new CalendarData { Tasks = [new(Guid.NewGuid(), "Segunda", new DateOnly(2026, 10, 9), false)] };

        store.Save(first);
        store.Save(second);

        Assert.Equal("Segunda", Assert.Single(store.Load().Tasks).Title);
        Assert.False(File.Exists(PathToCalendar + ".tmp"));
    }
}

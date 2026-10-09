using Island.Core.Abstractions;
using Island.Core.Calendar;

namespace Island.Core.Tests;

public sealed class CalendarAgendaTests
{
    [Fact]
    public void Adds_items_and_returns_day_items_in_a_stable_order()
    {
        var store = new MemoryCalendarStore();
        var agenda = new CalendarAgenda(store);
        var date = new DateOnly(2026, 10, 9);
        var late = agenda.AddEvent("Reunião tarde", date, new TimeOnly(15, 0));
        var allDay = agenda.AddEvent("Dia inteiro", date, null);
        var early = agenda.AddEvent("Reunião cedo", date, new TimeOnly(9, 0));
        var completed = agenda.AddTask("Concluída", date);
        agenda.SetTaskCompleted(completed.Id, true);
        var pendingB = agenda.AddTask("beta", date);
        var pendingA = agenda.AddTask("Alfa", date);
        var birthday = agenda.AddBirthday("Pessoa", date.Month, date.Day);

        CalendarDayItems day = agenda.GetDay(date);

        Assert.Equal(new[] { pendingA.Id, pendingB.Id, completed.Id }, day.Tasks.Select(task => task.Id));
        Assert.Equal(new[] { birthday.Id }, day.Birthdays.Select(item => item.Id));
        Assert.Equal(new[] { allDay.Id, early.Id, late.Id }, day.Events.Select(item => item.Id));
        Assert.Contains(date, agenda.GetMarkedDates(date.Year, date.Month));
        Assert.True(agenda.HasPendingTasks);
    }

    [Fact]
    public void Birthdays_repeat_each_year_and_leap_day_is_marked_only_when_valid()
    {
        var agenda = new CalendarAgenda(new MemoryCalendarStore());
        agenda.AddBirthday("Normal", 3, 14);
        agenda.AddBirthday("Leap", 2, 29);

        Assert.Single(agenda.GetDay(new DateOnly(2027, 3, 14)).Birthdays);
        Assert.Single(agenda.GetDay(new DateOnly(2028, 2, 29)).Birthdays);
        Assert.DoesNotContain(new DateOnly(2027, 2, 28), agenda.GetMarkedDates(2027, 2));
        Assert.Contains(new DateOnly(2028, 2, 29), agenda.GetMarkedDates(2028, 2));
    }

    [Fact]
    public void Failed_save_does_not_publish_state_or_raise_changed()
    {
        var store = new MemoryCalendarStore { FailSave = true };
        var agenda = new CalendarAgenda(store);
        int changes = 0;
        agenda.Changed += () => changes++;

        Assert.Throws<IOException>(() => agenda.AddTask("Task", new DateOnly(2026, 10, 9)));

        Assert.False(agenda.HasPendingTasks);
        Assert.Empty(agenda.GetDay(new DateOnly(2026, 10, 9)).Tasks);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Failed_completion_save_keeps_task_pending_and_does_not_raise_changed()
    {
        var store = new MemoryCalendarStore();
        var agenda = new CalendarAgenda(store);
        DateOnly date = new(2026, 10, 9);
        CalendarTask task = agenda.AddTask("Task", date);
        int changes = 0;
        agenda.Changed += () => changes++;
        store.FailSave = true;

        Assert.Throws<IOException>(() => agenda.SetTaskCompleted(task.Id, true));

        Assert.True(agenda.HasPendingTasks);
        Assert.False(Assert.Single(agenda.GetDay(date).Tasks).IsCompleted);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Future_tasks_count_as_pending_and_can_be_reopened()
    {
        var agenda = new CalendarAgenda(new MemoryCalendarStore());
        CalendarTask task = agenda.AddTask("Future", new DateOnly(2027, 1, 1));

        Assert.True(agenda.HasPendingTasks);
        Assert.True(agenda.SetTaskCompleted(task.Id, true));
        Assert.False(agenda.HasPendingTasks);
        Assert.True(agenda.SetTaskCompleted(task.Id, false));
        Assert.True(agenda.HasPendingTasks);
    }

    [Fact]
    public void Failed_load_keeps_agenda_read_only_to_protect_saved_data()
    {
        var store = new MemoryCalendarStore { FailLoad = true };
        var agenda = new CalendarAgenda(store);

        Assert.False(agenda.IsAvailable);
        Assert.IsType<UnauthorizedAccessException>(agenda.LoadError);
        Assert.Throws<IOException>(() => agenda.AddTask("Task", new DateOnly(2026, 10, 9)));
        Assert.Throws<IOException>(() => agenda.AddEvent("Event", new DateOnly(2026, 10, 9), null));
        Assert.Throws<IOException>(() => agenda.AddBirthday("Birthday", 10, 9));
        Assert.Throws<IOException>(() => agenda.SetTaskCompleted(Guid.NewGuid(), true));
        Assert.Null(store.Data);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public void Duplicate_or_empty_loaded_ids_are_repaired_before_task_updates()
    {
        Guid duplicateId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 9);
        var store = new MemoryCalendarStore
        {
            Data = new CalendarData
            {
                Tasks =
                [
                    new CalendarTask(duplicateId, "Primeira", date, false),
                    new CalendarTask(duplicateId, "Segunda", date, false),
                    new CalendarTask(Guid.Empty, "Terceira", date, false),
                ],
            },
        };
        var agenda = new CalendarAgenda(store);
        CalendarTask[] loadedTasks = agenda.GetDay(date).Tasks.ToArray();

        Assert.Equal(3, loadedTasks.Select(task => task.Id).Distinct().Count());
        Assert.All(loadedTasks, task => Assert.NotEqual(Guid.Empty, task.Id));
        Assert.True(agenda.SetTaskCompleted(loadedTasks[1].Id, true));
        Assert.Equal(new[] { false, true, false }, agenda.GetDay(date).Tasks.OrderBy(task => task.Title).Select(task => task.IsCompleted));
        CalendarData persisted = store.Data ?? throw new InvalidOperationException("The update was not saved.");
        Assert.Equal(3, persisted.Tasks.Select(task => task.Id).Distinct().Count());
    }

    [Fact]
    public void Normalizes_loaded_names_and_ignores_invalid_loaded_birthdays()
    {
        var date = new DateOnly(2026, 10, 9);
        var store = new MemoryCalendarStore
        {
            Data = new CalendarData
            {
                Tasks = [new(Guid.NewGuid(), "  tarefa  ", date, false), new(Guid.NewGuid(), "  ", date, false)],
                Events = [new(Guid.NewGuid(), "  evento  ", date, null)],
                Birthdays = [new(Guid.NewGuid(), "  inválido  ", 2, 30)],
            },
        };

        var agenda = new CalendarAgenda(store);

        Assert.Equal("tarefa", Assert.Single(agenda.GetDay(date).Tasks).Title);
        Assert.Equal("evento", Assert.Single(agenda.GetDay(date).Events).Title);
        Assert.Empty(agenda.GetDay(date).Birthdays);
    }

    private sealed class MemoryCalendarStore : ICalendarStore
    {
        public CalendarData? Data { get; set; }
        public bool FailSave { get; set; }
        public bool FailLoad { get; init; }
        public int SaveCount { get; private set; }

        public CalendarData Load() => FailLoad ? throw new UnauthorizedAccessException("Simulated read failure.") : Data ?? CalendarData.Empty;

        public void Save(CalendarData data)
        {
            if (FailSave) throw new IOException("Simulated storage failure.");
            Data = data;
            SaveCount++;
        }
    }
}

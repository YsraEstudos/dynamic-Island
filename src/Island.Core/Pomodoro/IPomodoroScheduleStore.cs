namespace Island.Core.Pomodoro;

public interface IPomodoroScheduleStore
{
    IReadOnlyList<ScheduledStart> Load();
    void Save(IReadOnlyList<ScheduledStart> starts);
}

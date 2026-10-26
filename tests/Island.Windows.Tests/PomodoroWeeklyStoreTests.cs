using Island.Core.Pomodoro;
using Island.Windows.Pomodoro;

namespace Island.Windows.Tests;

public sealed class PomodoroWeeklyStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"weekly-pomodoro-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp");
    }

    [Fact]
    public void A_block_saved_before_the_lengths_were_stored_loads_with_the_default_lengths()
    {
        File.WriteAllText(_path, """
            {"blocks":[{"id":"6f1c9a3e-1111-4b2a-9c3d-000000000001","day":"Monday","start":"09:00","end":"10:00",
            "mode":"Focus","cycles":2,"name":"Antigo"}],"enabled":true}
            """);

        PomodoroWeeklyBlock block = Assert.Single(new JsonPomodoroWeeklyScheduleStore(_path).Load().Blocks);

        Assert.Equal("Antigo", block.Name);
        Assert.Equal(PomodoroTimer.DefaultFocusMinutes, block.FocusMinutes);
        Assert.Equal(PomodoroTimer.DefaultBreakMinutes, block.BreakMinutes);
    }

    [Fact]
    public void Focus_and_break_lengths_survive_a_save_and_load()
    {
        var store = new JsonPomodoroWeeklyScheduleStore(_path);
        var block = new PomodoroWeeklyBlock(Guid.NewGuid(), DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(11, 0),
            ScheduledStartMode.Angry, "Novo", 40, 10);

        store.Save(new PomodoroWeeklyScheduleState([block], Enabled: true, SkippedDate: null, ResumePromptPending: false));
        PomodoroWeeklyBlock loaded = Assert.Single(new JsonPomodoroWeeklyScheduleStore(_path).Load().Blocks);

        Assert.Equal(block.Id, loaded.Id);
        Assert.Equal(40, loaded.FocusMinutes);
        Assert.Equal(10, loaded.BreakMinutes);
        Assert.Equal(ScheduledStartMode.Angry, loaded.Mode);
        Assert.Equal(2, loaded.Cycles);
    }
}

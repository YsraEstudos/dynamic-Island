using Island.Core.Pomodoro;
using Island.Windows.Pomodoro;

namespace Island.Windows.Tests;

public sealed class AngryResumeStoreTests : IDisposable
{
    private static readonly DateTimeOffset SavedAt = new(2026, 10, 27, 10, 1, 0, TimeSpan.FromHours(-3));
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"angry-resume-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp");
    }

    private static AngryResumeState Sample() => new(
        new PomodoroTimerState(PomodoroPhase.Focus, TimeSpan.FromMinutes(25), TimeSpan.FromSeconds(40), true, 4, 2,
            TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), true, "Revisar matemática", false),
        Locked: true, Phrase: "Eu desisto do meu foco agora, e sei disso.", Scheduled: false);

    [Fact]
    public void A_saved_session_is_taken_once_and_comes_back_unchanged()
    {
        var store = new JsonAngryResumeStore(_path);
        Assert.True(store.TrySave(Sample(), SavedAt));

        Assert.Equal(Sample(), store.Take(SavedAt.AddSeconds(30), MaxAge));
        Assert.Null(store.Take(SavedAt.AddSeconds(30), MaxAge));
    }

    [Fact]
    public void A_session_saved_too_long_ago_is_dropped()
    {
        var store = new JsonAngryResumeStore(_path);
        Assert.True(store.TrySave(Sample(), SavedAt));

        Assert.Null(store.Take(SavedAt + MaxAge + TimeSpan.FromSeconds(1), MaxAge));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void An_unreadable_file_is_dropped_without_throwing()
    {
        File.WriteAllText(_path, "{ not json");

        Assert.Null(new JsonAngryResumeStore(_path).Take(SavedAt, MaxAge));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Nothing_is_taken_when_no_session_was_saved()
    {
        Assert.Null(new JsonAngryResumeStore(_path).Take(SavedAt, MaxAge));
    }
}

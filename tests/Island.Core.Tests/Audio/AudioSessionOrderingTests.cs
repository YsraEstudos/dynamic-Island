using Island.Core.Audio;

namespace Island.Core.Tests.Audio;

public sealed class AudioSessionOrderingTests
{
    private static AppAudioSession Session(string id, string name, int pid, bool active, double peak = 0.0) =>
        new(id, name, pid, name.ToLowerInvariant(), 50, false, peak, active);

    [Fact]
    public void Foreground_app_comes_first_even_when_silent()
    {
        var sessions = new[]
        {
            Session("a", "Spotify", 10, active: true, peak: 0.9),
            Session("b", "Game", 20, active: false),
        };

        IReadOnlyList<AppAudioSession> ordered = AudioSessionOrdering.Order(sessions, foregroundProcessId: 20);

        Assert.Equal("Game", ordered[0].Name);
    }

    [Fact]
    public void Playing_sessions_come_before_inactive_ones()
    {
        var sessions = new[]
        {
            Session("z", "Zed", 1, active: false),
            Session("m", "Mail", 2, active: true),
            Session("c", "Calendar", 3, active: false),
        };

        IReadOnlyList<AppAudioSession> ordered = AudioSessionOrdering.Order(sessions, foregroundProcessId: 0);

        Assert.Equal(new[] { "Mail", "Calendar", "Zed" }, ordered.Select(s => s.Name));
    }

    [Fact]
    public void Peak_level_does_not_reorder_rows()
    {
        var quiet = new[] { Session("a", "Alpha", 1, true, 0.0), Session("b", "Beta", 2, true, 0.0) };
        var loud = new[] { Session("a", "Alpha", 1, true, 0.1), Session("b", "Beta", 2, true, 0.95) };

        Assert.Equal(
            AudioSessionOrdering.Order(quiet, 0).Select(s => s.Id),
            AudioSessionOrdering.Order(loud, 0).Select(s => s.Id));
    }

    [Fact]
    public void Ties_are_broken_by_name_then_process_id()
    {
        var sessions = new[]
        {
            Session("x", "chrome", 30, true),
            Session("y", "Chrome", 20, true),
            Session("w", "Alpha", 40, true),
        };

        IReadOnlyList<AppAudioSession> ordered = AudioSessionOrdering.Order(sessions, 0);

        Assert.Equal(new[] { "w", "y", "x" }, ordered.Select(s => s.Id));
    }

    [Fact]
    public void Unknown_foreground_zero_does_not_promote_anything()
    {
        var sessions = new[] { Session("a", "Beta", 0, true), Session("b", "Alpha", 5, true) };

        IReadOnlyList<AppAudioSession> ordered = AudioSessionOrdering.Order(sessions, foregroundProcessId: 0);

        Assert.Equal("Alpha", ordered[0].Name);
    }

    [Fact]
    public void Order_returns_every_session_once()
    {
        var sessions = Enumerable.Range(1, 9).Select(i => Session($"id{i}", $"App{i}", i, i % 2 == 0)).ToList();

        IReadOnlyList<AppAudioSession> ordered = AudioSessionOrdering.Order(sessions, foregroundProcessId: 4);

        Assert.Equal(sessions.Count, ordered.Count);
        Assert.Equal(sessions.Select(s => s.Id).OrderBy(id => id), ordered.Select(s => s.Id).OrderBy(id => id));
    }
}

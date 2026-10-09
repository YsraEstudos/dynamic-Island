using Island.Core.Audio;

namespace Island.Core.Tests.Audio;

public sealed class AudioSessionMatcherAndPagingTests
{
    private static AppAudioSession Session(string id, string processName) =>
        new(id, processName, 1, processName, 50, false, 0.0, true);

    [Theory]
    [InlineData("Discord")]
    [InlineData("discord.exe")]
    [InlineData("DISCORD")]
    public void Matcher_ignores_case_and_exe_suffix(string query)
    {
        var sessions = new[] { Session("1", "Discord"), Session("2", "Spotify") };

        IReadOnlyList<AppAudioSession> found = AudioSessionMatcher.FindByProcessName(sessions, query);

        Assert.Equal(new[] { "1" }, found.Select(s => s.Id));
    }

    [Fact]
    public void Matcher_returns_every_session_of_the_process_and_none_when_absent()
    {
        var sessions = new[] { Session("1", "chrome"), Session("2", "chrome"), Session("3", "Spotify") };

        Assert.Equal(2, AudioSessionMatcher.FindByProcessName(sessions, "Chrome").Count);
        Assert.Empty(AudioSessionMatcher.FindByProcessName(sessions, "Discord"));
        Assert.Empty(AudioSessionMatcher.FindByProcessName(sessions, "   "));
    }

    [Fact]
    public void MaxOffset_is_count_minus_page_and_never_negative()
    {
        Assert.Equal(0, AudioListPaging.MaxOffset(0));
        Assert.Equal(0, AudioListPaging.MaxOffset(3));
        Assert.Equal(2, AudioListPaging.MaxOffset(5));
    }

    [Fact]
    public void Step_stays_inside_the_list()
    {
        Assert.Equal(0, AudioListPaging.Step(0, -1, count: 5));
        Assert.Equal(1, AudioListPaging.Step(0, 1, count: 5));
        Assert.Equal(2, AudioListPaging.Step(2, 1, count: 5));
        Assert.Equal(2, AudioListPaging.Step(1, 9, count: 5));
    }

    [Fact]
    public void Clamp_pulls_an_offset_back_after_rows_disappear()
    {
        Assert.Equal(0, AudioListPaging.Clamp(4, count: 3));
        Assert.Equal(1, AudioListPaging.Clamp(4, count: 4));
    }

    [Theory]
    [InlineData(0, 5, "1–3 de 5")]
    [InlineData(2, 5, "3–5 de 5")]
    [InlineData(0, 3, "")]
    [InlineData(0, 0, "")]
    public void Label_shows_the_rows_on_screen(int offset, int count, string expected)
    {
        Assert.Equal(expected, AudioListPaging.Label(offset, count));
    }
}

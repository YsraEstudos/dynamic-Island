using Island.Core.Audio;

namespace Island.Core.Tests.Audio;

public sealed class FriendlyAppNameTests
{
    [Fact]
    public void File_description_wins_over_session_name_and_process_name()
    {
        Assert.Equal("Discord", FriendlyAppName.Resolve("Discord", "Some tab title", "Discord"));
    }

    [Fact]
    public void Session_display_name_is_used_when_there_is_no_file_description()
    {
        Assert.Equal("Minha Música", FriendlyAppName.Resolve(" ", "Minha Música ", "player"));
    }

    [Fact]
    public void Resource_references_are_not_shown_as_names()
    {
        Assert.Equal("Spotify", FriendlyAppName.Resolve(null, "@%SystemRoot%\\resources.pri,-1", "Spotify"));
    }

    [Theory]
    [InlineData("chrome", "Chrome")]
    [InlineData("EpicGamesLauncher", "Epic Games Launcher")]
    [InlineData("VALORANT-Win64-Shipping", "VALORANT Win64 Shipping")]
    [InlineData("discord.exe", "Discord")]
    [InlineData("my_tool", "My Tool")]
    public void Process_name_becomes_words(string processName, string expected)
    {
        Assert.Equal(expected, FriendlyAppName.Resolve(null, null, processName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_known_falls_back_to_a_generic_name(string? processName)
    {
        Assert.Equal(FriendlyAppName.Fallback, FriendlyAppName.Resolve(null, null, processName));
    }
}

using Island.Core.GameNotes;

namespace Island.Core.Tests;

public sealed class GameDetectionRulesTests
{
    private static readonly IReadOnlyList<string> NoGames = Array.Empty<string>();

    [Theory]
    [InlineData("Celeste", "celeste")]
    [InlineData("  Celeste.EXE  ", "celeste")]
    [InlineData("eldenring.exe", "eldenring")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_strips_exe_and_case(string? processName, string expected)
    {
        Assert.Equal(expected, GameKey.Normalize(processName));
    }

    [Fact]
    public void GameInfo_key_is_the_normalized_process_name()
    {
        var game = new GameInfo("Celeste.exe", "Celeste", null);

        Assert.Equal("celeste", game.Key);
    }

    [Theory]
    [InlineData("EXPLORER.exe", true)]
    [InlineData("chrome", true)]
    [InlineData("Celeste", false)]
    public void Ignore_list_matches_normalized_names(string processName, bool ignored)
    {
        Assert.Equal(ignored, GameDetectionRules.IsIgnored(processName));
    }

    [Fact]
    public void Fullscreen_unknown_process_is_a_game()
    {
        ForegroundProcess process = Window("celeste", fullscreen: true, description: "Celeste");

        GameInfo? game = GameDetectionRules.Detect(process, NoGames);

        Assert.NotNull(game);
        Assert.Equal("celeste", game.Key);
        Assert.Equal("Celeste", game.DisplayName);
    }

    [Fact]
    public void Windowed_unknown_process_is_not_a_game_so_the_last_game_stays()
    {
        ForegroundProcess process = Window("notepad2", fullscreen: false, description: "Notepad2");

        Assert.Null(GameDetectionRules.Detect(process, NoGames));
    }

    [Fact]
    public void Fullscreen_ignored_app_is_not_a_game()
    {
        // A browser video or a presentation going fullscreen must not become a game.
        ForegroundProcess process = Window("chrome", fullscreen: true, description: "Google Chrome");

        Assert.Null(GameDetectionRules.Detect(process, NoGames));
    }

    [Fact]
    public void Configured_process_is_a_game_even_windowed()
    {
        ForegroundProcess process = Window("MyGame", fullscreen: false, description: "My Game");

        GameInfo? game = GameDetectionRules.Detect(process, new[] { "mygame.exe" });

        Assert.NotNull(game);
        Assert.Equal("mygame", game.Key);
    }

    [Fact]
    public void Configured_process_beats_the_ignore_list()
    {
        ForegroundProcess process = Window("chrome", fullscreen: false, description: "Google Chrome");

        Assert.NotNull(GameDetectionRules.Detect(process, new[] { "Chrome" }));
    }

    [Fact]
    public void Shell_and_own_windows_are_never_games()
    {
        Assert.Null(GameDetectionRules.Detect(Window("explorer", fullscreen: true, shell: true), new[] { "explorer" }));
        Assert.Null(GameDetectionRules.Detect(Window("DynamicIsland", fullscreen: true, own: true), new[] { "DynamicIsland" }));
    }

    [Fact]
    public void Empty_process_name_is_not_a_game()
    {
        Assert.Null(GameDetectionRules.Detect(Window("", fullscreen: true), NoGames));
    }

    [Fact]
    public void Display_name_falls_back_to_window_title_then_process_name()
    {
        ForegroundProcess titled = Window("game", fullscreen: true, description: "   ", title: "Game Title");
        ForegroundProcess bare = Window("game", fullscreen: true, description: null, title: null);

        Assert.Equal("Game Title", GameDetectionRules.Detect(titled, NoGames)!.DisplayName);
        Assert.Equal("game", GameDetectionRules.Detect(bare, NoGames)!.DisplayName);
    }

    [Fact]
    public void Display_name_is_trimmed_to_a_sane_length()
    {
        string longName = new('x', 200);
        ForegroundProcess process = Window("game", fullscreen: true, description: "  " + longName + "  ");

        string name = GameDetectionRules.Detect(process, NoGames)!.DisplayName;

        Assert.Equal(80, name.Length);
    }

    private static ForegroundProcess Window(
        string processName,
        bool fullscreen,
        string? description = null,
        string? title = null,
        bool shell = false,
        bool own = false) =>
        new(processName, ExePath: processName.Length == 0 ? null : @"C:\Games\" + processName + ".exe",
            FileDescription: description, WindowTitle: title,
            IsFullscreen: fullscreen, IsShellWindow: shell, IsOwnProcess: own);
}

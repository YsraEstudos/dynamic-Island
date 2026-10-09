using Island.Core.GameNotes;

namespace Island.Core.Tests;

public sealed class GameNotesSelectionTests
{
    private static readonly GameInfo Celeste = new("celeste", "Celeste", null);
    private static readonly GameInfo EldenRing = new("eldenring", "ELDEN RING", null);
    private static readonly GameInfo Hades = new("hades", "Hades", null);

    [Fact]
    public void Observing_a_game_reports_the_change_once()
    {
        var selection = new GameNotesSelection();

        Assert.True(selection.ObserveGame(Celeste));
        Assert.False(selection.ObserveGame(Celeste));
        Assert.Equal("celeste", selection.ResolvedKey);
    }

    [Fact]
    public void The_last_game_stays_when_no_other_game_is_observed()
    {
        // Focus moves to the island: nothing is observed, so the last game keeps showing.
        var selection = new GameNotesSelection();
        selection.ObserveGame(Celeste);

        Assert.Equal("celeste", selection.ResolvedKey);
        Assert.Equal(Celeste, selection.LastGame);
    }

    [Fact]
    public void Resolved_key_prefers_pinned_then_browsed_then_last_game()
    {
        var selection = new GameNotesSelection();
        selection.ObserveGame(Celeste);
        Assert.Equal("celeste", selection.ResolvedKey);

        selection.Browse("hades");
        Assert.Equal("hades", selection.ResolvedKey);

        selection.TogglePinned("eldenring");
        Assert.Equal("eldenring", selection.ResolvedKey);
    }

    [Fact]
    public void A_new_game_ends_browsing_but_keeps_the_pin()
    {
        var selection = new GameNotesSelection();
        selection.ObserveGame(Celeste);
        selection.Browse("hades");
        selection.TogglePinned("eldenring");

        selection.ObserveGame(Hades);

        Assert.Null(selection.BrowsedKey);
        Assert.Equal("eldenring", selection.PinnedKey);
        Assert.Equal("eldenring", selection.ResolvedKey);
    }

    [Fact]
    public void Step_wraps_around_and_browses_to_the_next_game()
    {
        var selection = new GameNotesSelection();
        selection.ObserveGame(Celeste);
        string[] keys = ["celeste", "eldenring", "hades"];

        Assert.Equal("eldenring", selection.Step(keys, 1));
        Assert.Equal("hades", selection.Step(keys, 1));
        Assert.Equal("celeste", selection.Step(keys, 1));
        Assert.Equal("hades", selection.Step(keys, -1));
    }

    [Fact]
    public void Step_from_a_game_outside_the_list_starts_at_either_end()
    {
        var selection = new GameNotesSelection();
        selection.ObserveGame(Celeste);
        string[] keys = ["eldenring", "hades"];

        Assert.Equal("eldenring", selection.Step(keys, 1));
        Assert.Null(new GameNotesSelection().Step(Array.Empty<string>(), 1));
    }

    [Fact]
    public void Stepping_while_pinned_moves_the_pin_along()
    {
        var selection = new GameNotesSelection();
        selection.ObserveGame(Celeste);
        selection.TogglePinned("celeste");
        string[] keys = ["celeste", "hades"];

        selection.Step(keys, 1);

        Assert.Equal("hades", selection.PinnedKey);
        Assert.Equal("hades", selection.ResolvedKey);
    }

    [Fact]
    public void Toggle_pinned_pins_then_unpins_the_same_key()
    {
        var selection = new GameNotesSelection();

        Assert.True(selection.TogglePinned("celeste"));
        Assert.False(selection.TogglePinned("celeste"));
        Assert.Null(selection.PinnedKey);
        Assert.False(selection.TogglePinned(null));
    }
}

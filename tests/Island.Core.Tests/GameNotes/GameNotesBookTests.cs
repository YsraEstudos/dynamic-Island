using Island.Core.GameNotes;

namespace Island.Core.Tests;

public sealed class GameNotesBookTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly GameInfo Celeste = new("celeste", "Celeste", @"C:\Games\Celeste.exe");

    [Fact]
    public void Add_files_the_note_under_the_game_key()
    {
        GameNote note = Note("Pegar a fita", T0);

        GameNotesBook book = new GameNotesBook().WithNote(Celeste, note, T0)!;

        GameNotesGame game = Assert.Single(book.Games);
        Assert.Equal("celeste", game.Key);
        Assert.Equal("Celeste", game.DisplayName);
        Assert.Equal(@"C:\Games\Celeste.exe", game.ExePath);
        Assert.Equal(note, Assert.Single(book.NotesFor("celeste")));
    }

    [Fact]
    public void Add_rejects_blank_text()
    {
        Assert.Null(new GameNotesBook().WithNote(Celeste, Note("   ", T0), T0));
    }

    [Fact]
    public void Notes_show_pinned_first_then_open_then_newest()
    {
        var oldOpen = Note("old open", T0);
        var newOpen = Note("new open", T0.AddMinutes(10));
        var done = Note("done", T0.AddMinutes(20)) with { IsCompleted = true };
        var pinned = Note("pinned old", T0.AddMinutes(-30)) with { IsPinned = true };

        GameNotesBook book = new GameNotesBook()
            .WithNote(Celeste, oldOpen, T0)!
            .WithNote(Celeste, newOpen, T0)!
            .WithNote(Celeste, done, T0)!
            .WithNote(Celeste, pinned, T0)!;

        Assert.Equal(
            new[] { "pinned old", "new open", "old open", "done" },
            book.NotesFor("celeste").Select(note => note.Text).ToArray());
    }

    [Fact]
    public void Toggle_flips_completed_and_moves_the_note_down()
    {
        GameNote first = Note("first", T0);
        GameNote second = Note("second", T0.AddMinutes(1));
        GameNotesBook book = new GameNotesBook().WithNote(Celeste, first, T0)!.WithNote(Celeste, second, T0)!;

        GameNotesBook next = book.WithToggledCompleted("celeste", second.Id, T0.AddMinutes(2));

        Assert.True(next.NotesFor("celeste").Single(note => note.Id == second.Id).IsCompleted);
        Assert.Equal("first", next.NotesFor("celeste")[0].Text);
        Assert.Equal(T0.AddMinutes(2), next.Find("celeste")!.UpdatedAt);
    }

    [Fact]
    public void Toggle_of_a_missing_note_returns_the_same_book()
    {
        GameNotesBook book = new GameNotesBook().WithNote(Celeste, Note("x", T0), T0)!;

        Assert.Same(book, book.WithToggledCompleted("celeste", Guid.NewGuid(), T0));
        Assert.Same(book, book.WithToggledCompleted("missing-game", Guid.NewGuid(), T0));
    }

    [Fact]
    public void Pin_to_the_current_value_returns_the_same_book()
    {
        GameNote note = Note("x", T0);
        GameNotesBook book = new GameNotesBook().WithNote(Celeste, note, T0)!;

        Assert.Same(book, book.WithPinned("celeste", note.Id, pinned: false, T0));
        Assert.True(book.WithPinned("celeste", note.Id, pinned: true, T0).NotesFor("celeste")[0].IsPinned);
    }

    [Fact]
    public void Removing_the_last_note_removes_the_game()
    {
        GameNote note = Note("x", T0);
        GameNotesBook book = new GameNotesBook().WithNote(Celeste, note, T0)!;

        GameNotesBook next = book.WithoutNote("celeste", note.Id, T0);

        Assert.Empty(next.Games);
        Assert.Null(next.Find("celeste"));
        Assert.Single(book.Games);
    }

    [Fact]
    public void Removing_one_of_several_notes_keeps_the_others()
    {
        GameNote keep = Note("keep", T0);
        GameNote drop = Note("drop", T0.AddMinutes(1));
        GameNotesBook book = new GameNotesBook().WithNote(Celeste, keep, T0)!.WithNote(Celeste, drop, T0)!;

        GameNotesBook next = book.WithoutNote("celeste", drop.Id, T0);

        Assert.Equal(new[] { keep.Id }, next.NotesFor("celeste").Select(note => note.Id).ToArray());
    }

    [Fact]
    public void Each_game_is_limited_to_the_maximum_number_of_notes()
    {
        GameNotesBook book = new GameNotesBook();
        for (int i = 0; i < GameNotesBook.MaxNotesPerGame; i++)
        {
            book = book.WithNote(Celeste, Note("note " + i, T0.AddSeconds(i)), T0)!;
        }

        Assert.Null(book.WithNote(Celeste, Note("one too many", T0), T0));
        Assert.Equal(GameNotesBook.MaxNotesPerGame, book.NotesFor("celeste").Count);
    }

    [Fact]
    public void Games_are_ordered_by_most_recent_change()
    {
        var eldenring = new GameInfo("eldenring", "ELDEN RING", null);
        GameNotesBook book = new GameNotesBook()
            .WithNote(Celeste, Note("a", T0), T0)!
            .WithNote(eldenring, Note("b", T0.AddHours(1)), T0.AddHours(1))!;

        Assert.Equal(new[] { "eldenring", "celeste" }, book.Games.Select(game => game.Key).ToArray());
    }

    [Fact]
    public void Game_info_refresh_only_changes_the_book_when_something_differs()
    {
        GameNotesBook book = new GameNotesBook().WithNote(Celeste, Note("x", T0), T0)!;

        Assert.Same(book, book.WithGameInfo(Celeste));

        GameNotesBook renamed = book.WithGameInfo(Celeste with { DisplayName = "Celeste (2018)" });
        Assert.Equal("Celeste (2018)", renamed.Find("celeste")!.DisplayName);
        Assert.Equal(T0, renamed.Find("celeste")!.UpdatedAt);
    }

    [Fact]
    public void Game_info_for_a_game_without_notes_is_ignored()
    {
        GameNotesBook book = new GameNotesBook();

        Assert.Same(book, book.WithGameInfo(Celeste));
    }

    [Fact]
    public void Constructor_skips_empty_games_and_empty_keys()
    {
        var book = new GameNotesBook(new[]
        {
            new GameNotesGame("celeste", "celeste", "Celeste", null, T0, new[] { Note("x", T0) }),
            new GameNotesGame("empty", "empty", "Empty", null, T0, Array.Empty<GameNote>()),
            new GameNotesGame(string.Empty, "", "Nameless", null, T0, new[] { Note("y", T0) }),
        });

        Assert.Equal(new[] { "celeste" }, book.Games.Select(game => game.Key).ToArray());
    }

    [Fact]
    public void Clean_trims_and_caps_the_text()
    {
        Assert.Equal("nota", GameNoteText.Clean("  nota  "));
        Assert.Null(GameNoteText.Clean(" \t "));
        Assert.Null(GameNoteText.Clean(null));
        Assert.Equal(GameNotesBook.MaxTextLength, GameNoteText.Clean(new string('a', 2000))!.Length);
    }

    private static GameNote Note(string text, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), text, createdAt, IsPinned: false, IsCompleted: false);
}

using Island.Core.Fakes;
using Island.Core.GameNotes;

namespace Island.Core.Tests;

public sealed class GameNotesServiceTests
{
    private static readonly GameInfo Celeste = new("celeste", "Celeste", null);
    private static readonly GameInfo EldenRing = new("eldenring", "ELDEN RING", null);

    [Fact]
    public async Task Adding_before_the_notes_load_is_refused_so_the_stored_notes_are_not_overwritten()
    {
        var store = new InMemoryGameNotesStore(new[] { Stored("celeste", "keep me") });
        var service = new GameNotesService(store, new FakeForegroundGameTracker(Celeste));

        bool saved = await service.AddNoteAsync("celeste", "new note");

        Assert.False(saved);
        Assert.False(service.IsInitialized);
        Assert.Equal("keep me", Assert.Single((await store.LoadAsync())[0].Notes).Text);
    }

    [Fact]
    public async Task Initialize_loads_the_stored_notes_and_raises_changed()
    {
        var store = new InMemoryGameNotesStore(new[] { Stored("celeste", "fita") });
        var service = new GameNotesService(store, new FakeForegroundGameTracker());
        int changes = 0;
        service.Changed += () => changes++;

        await service.InitializeAsync();
        await service.InitializeAsync();

        Assert.True(service.IsInitialized);
        Assert.Single(service.GamesWithNotes);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task A_note_is_filed_under_the_game_on_screen_and_saved()
    {
        var store = new InMemoryGameNotesStore();
        var tracker = new FakeForegroundGameTracker();
        var service = new GameNotesService(store, tracker);
        await service.InitializeAsync();

        tracker.Bring(Celeste);
        bool saved = await service.AddNoteAsync(service.ResolvedKey!, "  Pegar a fita  ");

        Assert.True(saved);
        Assert.Equal("Pegar a fita", Assert.Single(service.NotesFor("celeste")).Text);
        Assert.Equal("Pegar a fita", Assert.Single((await store.LoadAsync())[0].Notes).Text);
    }

    [Fact]
    public async Task A_failed_save_keeps_the_previous_notes_and_reports_the_error()
    {
        var store = new FailingGameNotesStore();
        var service = new GameNotesService(store, new FakeForegroundGameTracker(Celeste));
        await service.InitializeAsync();
        store.FailNextSave = true;
        int changes = 0;
        service.Changed += () => changes++;

        bool saved = await service.AddNoteAsync("celeste", "sem salvar");

        Assert.False(saved);
        Assert.True(service.HasSaveError);
        Assert.Empty(service.GamesWithNotes);
        Assert.Equal(1, changes);

        // The next write that succeeds clears the error.
        Assert.True(await service.AddNoteAsync("celeste", "agora vai"));
        Assert.False(service.HasSaveError);
    }

    [Fact]
    public async Task Bringing_another_game_moves_the_widget_to_it()
    {
        var tracker = new FakeForegroundGameTracker(Celeste);
        var service = new GameNotesService(new InMemoryGameNotesStore(), tracker);
        await service.InitializeAsync();
        int changes = 0;
        service.Changed += () => changes++;

        tracker.Bring(EldenRing);

        Assert.Equal("eldenring", service.ResolvedKey);
        Assert.True(changes >= 1);
    }

    [Fact]
    public async Task A_pinned_game_stays_on_screen_when_another_game_comes_to_the_front()
    {
        var tracker = new FakeForegroundGameTracker(Celeste);
        var service = new GameNotesService(new InMemoryGameNotesStore(), tracker);
        await service.InitializeAsync();

        service.TogglePinnedGame();
        tracker.Bring(EldenRing);

        Assert.True(service.IsPinned("celeste"));
        Assert.Equal("celeste", service.ResolvedKey);

        service.TogglePinnedGame();
        Assert.Equal("eldenring", service.ResolvedKey);
    }

    [Fact]
    public async Task Browsing_shows_the_next_game_that_has_notes()
    {
        var store = new InMemoryGameNotesStore(new[] { Stored("celeste", "a"), Stored("eldenring", "b") });
        var service = new GameNotesService(store, new FakeForegroundGameTracker(Celeste));
        await service.InitializeAsync();

        service.BrowseStep(1);

        Assert.Equal("eldenring", service.ResolvedKey);
        Assert.Equal("eldenring", service.DisplayNameFor("eldenring"));
    }

    [Fact]
    public async Task Last_game_is_remembered_when_the_service_starts_after_the_tracker_saw_it()
    {
        var tracker = new FakeForegroundGameTracker(Celeste);

        var service = new GameNotesService(new InMemoryGameNotesStore(), tracker);

        Assert.Equal("celeste", service.ResolvedKey);
        Assert.Equal("Celeste", service.DisplayNameFor("celeste"));
        await service.InitializeAsync();
    }

    [Fact]
    public async Task Removing_and_toggling_notes_update_the_saved_games()
    {
        var store = new InMemoryGameNotesStore();
        var tracker = new FakeForegroundGameTracker(Celeste);
        var service = new GameNotesService(store, tracker);
        await service.InitializeAsync();
        await service.AddNoteAsync("celeste", "um");
        Guid id = service.NotesFor("celeste")[0].Id;

        Assert.True(await service.ToggleNoteAsync("celeste", id));
        Assert.True(service.NotesFor("celeste")[0].IsCompleted);

        Assert.True(await service.SetNotePinnedAsync("celeste", id, pinned: true));
        Assert.True(service.NotesFor("celeste")[0].IsPinned);

        Assert.True(await service.RemoveNoteAsync("celeste", id));
        Assert.Empty(service.GamesWithNotes);
        Assert.Empty((await store.LoadAsync()));
    }

    [Fact]
    public async Task Unknown_note_changes_report_nothing_to_save()
    {
        var service = new GameNotesService(new InMemoryGameNotesStore(), new FakeForegroundGameTracker(Celeste));
        await service.InitializeAsync();

        Assert.False(await service.ToggleNoteAsync("celeste", Guid.NewGuid()));
        Assert.False(await service.AddNoteAsync("celeste", "   "));
    }

    private static GameNotesGame Stored(string key, string text) => new(
        key, key, key, null, DateTimeOffset.UtcNow,
        new[] { new GameNote(Guid.NewGuid(), text, DateTimeOffset.UtcNow, false, false) });

    private sealed class FailingGameNotesStore : IGameNotesStore
    {
        private IReadOnlyList<GameNotesGame> _games = Array.Empty<GameNotesGame>();

        public bool FailNextSave { get; set; }

        public Task<IReadOnlyList<GameNotesGame>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_games);

        public Task SaveAsync(IReadOnlyList<GameNotesGame> games, CancellationToken cancellationToken = default)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("disk full");
            }

            _games = games.ToArray();
            return Task.CompletedTask;
        }
    }
}

using Island.Core.Notes;

namespace Island.Core.Tests;

public sealed class QuickNotesServiceTests
{
    private static readonly QuickNoteDraft EmptyDraft = new(
        string.Empty,
        string.Empty,
        Array.Empty<QuickNoteChecklistItem>(),
        Array.Empty<string>(),
        QuickNoteColor.Default);

    private static readonly QuickNoteDraft DraftWithContent = new(
        "Título",
        "conteúdo",
        Array.Empty<QuickNoteChecklistItem>(),
        Array.Empty<string>(),
        QuickNoteColor.Default);

    [Fact]
    public async Task Blank_new_draft_is_not_persisted()
    {
        var service = await CreateServiceAsync();

        Assert.Null(await service.SaveDraftAsync(null, EmptyDraft));
        Assert.Empty(service.GetNotes(QuickNoteCollection.Active));
    }

    [Fact]
    public async Task Tags_alone_do_not_create_a_note()
    {
        var service = await CreateServiceAsync();
        var taggedEmptyDraft = EmptyDraft with { Tags = ["importante"] };

        Assert.Null(await service.SaveDraftAsync(null, taggedEmptyDraft));
        Assert.Empty(service.GetNotes());
    }

    [Fact]
    public async Task Checklist_text_can_create_a_note_without_title_or_body()
    {
        var service = await CreateServiceAsync();
        var checklistDraft = EmptyDraft with
        {
            Checklist = [new QuickNoteChecklistItem(Guid.NewGuid(), "Comprar leite", false, 0)]
        };

        var saved = await service.SaveDraftAsync(null, checklistDraft);

        Assert.Equal("Comprar leite", Assert.IsType<QuickNote>(saved).Checklist.Single().Text);
    }

    [Fact]
    public async Task Tags_are_trimmed_and_deduplicated_without_case_sensitivity()
    {
        var service = await CreateServiceAsync();
        var taggedDraft = DraftWithContent with { Tags = [" Leitura ", "leitura", " Trabalho "] };

        var saved = await service.SaveDraftAsync(null, taggedDraft);

        Assert.Equal(new[] { "Leitura", "Trabalho" }, Assert.IsType<QuickNote>(saved).Tags);
    }

    [Fact]
    public async Task Existing_note_with_empty_draft_remains_saved()
    {
        var (service, _) = await CreateServiceWithNoteAsync();

        await service.SaveDraftAsync(service.GetNotes().Single().Id, EmptyDraft);

        Assert.Single(service.GetNotes(QuickNoteCollection.Active));
    }

    [Fact]
    public async Task Search_matches_content_and_tags_without_case_sensitivity()
    {
        var (service, _) = await CreateServiceWithNoteAsync("café", ["Leitura"]);

        Assert.Single(service.GetNotes(QuickNoteCollection.Active, "CAFÉ"));
        Assert.Single(service.GetNotes(QuickNoteCollection.Active, "leitura"));
    }

    [Fact]
    public async Task Pinned_notes_sort_before_unpinned_then_by_updated_time()
    {
        var (service, newer, older, unpinned) = await CreateOrderingFixtureAsync();

        Assert.Equal(new[] { newer.Id, older.Id, unpinned.Id }, service.GetNotes().Select(note => note.Id));
    }

    [Fact]
    public async Task Trash_restore_and_permanent_delete_use_separate_collections()
    {
        var (service, note) = await CreateServiceWithNoteAsync();

        await service.MoveToTrashAsync(note.Id);
        Assert.Empty(service.GetNotes());
        Assert.Single(service.GetNotes(QuickNoteCollection.Trash));

        await service.RestoreFromTrashAsync(note.Id);
        Assert.Single(service.GetNotes());
        Assert.Empty(service.GetNotes(QuickNoteCollection.Trash));

        await service.DeletePermanentlyAsync(note.Id);
        Assert.Empty(service.GetNotes(QuickNoteCollection.Trash));
    }

    [Fact]
    public async Task Archived_note_leaves_active_and_returns_when_unarchived()
    {
        var (service, note) = await CreateServiceWithNoteAsync();

        await service.SetArchivedAsync(note.Id, true);
        Assert.Empty(service.GetNotes());
        Assert.Single(service.GetNotes(QuickNoteCollection.Archived));

        await service.SetArchivedAsync(note.Id, false);
        Assert.Single(service.GetNotes());
    }

    [Fact]
    public async Task Failed_store_write_does_not_publish_the_mutation()
    {
        var (service, store) = await CreateServiceAsyncWithStore();
        store.ThrowOnSave = true;

        await Assert.ThrowsAsync<IOException>(() => service.SaveDraftAsync(null, DraftWithContent));

        Assert.Empty(service.GetNotes());
    }

    private static async Task<QuickNotesService> CreateServiceAsync()
    {
        var (service, _) = await CreateServiceAsyncWithStore();
        return service;
    }

    private static async Task<(QuickNotesService Service, MemoryQuickNotesStore Store)> CreateServiceAsyncWithStore()
    {
        var store = new MemoryQuickNotesStore();
        var service = new QuickNotesService(store);
        await service.InitializeAsync();
        return (service, store);
    }

    private static async Task<(QuickNotesService Service, QuickNote Note)> CreateServiceWithNoteAsync(
        string content = "nota",
        IReadOnlyList<string>? tags = null)
    {
        var (service, _) = await CreateServiceAsyncWithStore();
        var saved = await service.SaveDraftAsync(null, new QuickNoteDraft(
            string.Empty,
            content,
            Array.Empty<QuickNoteChecklistItem>(),
            tags ?? Array.Empty<string>(),
            QuickNoteColor.Default));
        return (service, Assert.IsType<QuickNote>(saved));
    }

    private static async Task<(QuickNotesService Service, QuickNote Newer, QuickNote Older, QuickNote Unpinned)> CreateOrderingFixtureAsync()
    {
        var timeProvider = new IncrementingTimeProvider();
        var service = new QuickNotesService(new MemoryQuickNotesStore(), timeProvider);
        await service.InitializeAsync();

        var older = Assert.IsType<QuickNote>(await service.SaveDraftAsync(null, DraftWithContent));
        await service.SetPinnedAsync(older.Id, true);
        var newer = Assert.IsType<QuickNote>(await service.SaveDraftAsync(null, DraftWithContent));
        await service.SetPinnedAsync(newer.Id, true);
        var unpinned = Assert.IsType<QuickNote>(await service.SaveDraftAsync(null, DraftWithContent));

        return (service, newer, older, unpinned);
    }

    private sealed class MemoryQuickNotesStore : IQuickNotesStore
    {
        public IReadOnlyList<QuickNote> Notes { get; private set; } = Array.Empty<QuickNote>();
        public bool ThrowOnSave { get; set; }

        public Task<IReadOnlyList<QuickNote>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Notes);

        public Task SaveAsync(IReadOnlyList<QuickNote> notes, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave) throw new IOException("The test store rejected the write.");
            Notes = notes.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class IncrementingTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            var result = _now;
            _now = _now.AddMinutes(1);
            return result;
        }
    }
}

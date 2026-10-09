using Island.App.ViewModels;
using Island.Core.Configuration;
using Island.Core.Notes;

namespace Island.Windows.Tests;

public sealed class QuickNotesViewModelTests
{
    [Fact]
    public async Task New_empty_draft_is_not_saved_until_it_has_content()
    {
        var viewModel = await CreateViewModelAsync(TimeSpan.FromMilliseconds(10));

        viewModel.BeginNewCommand.Execute(null);
        await viewModel.FlushPendingSaveAsync();

        Assert.Empty(viewModel.Notes);
    }

    [Fact]
    public async Task Switching_notes_flushes_the_pending_debounced_edit()
    {
        var (viewModel, service, current, other) = await CreateViewModelWithTwoNotesAsync(TimeSpan.FromMilliseconds(10));

        viewModel.ContentDraft = "latest";
        await viewModel.SelectNoteCommand.ExecuteAsync(other.Id);

        Assert.Equal("latest", service.GetNotes().Single(note => note.Id == current.Id).Content);
    }

    [Fact]
    public async Task Explicit_flush_persists_before_the_debounce_expires()
    {
        var (viewModel, service, note) = await CreateViewModelWithNoteAsync(autosaveDelay: TimeSpan.FromSeconds(5));

        viewModel.ContentDraft = "closing now";
        await viewModel.FlushPendingSaveAsync();

        Assert.Equal("closing now", service.GetNotes().Single(item => item.Id == note.Id).Content);
    }

    [Fact]
    public async Task Save_failure_keeps_draft_and_sets_error_state()
    {
        var (viewModel, store) = await CreateViewModelWithStoreAsync(TimeSpan.FromMilliseconds(10));

        viewModel.BeginNewCommand.Execute(null);
        viewModel.ContentDraft = "draft intacto";
        store.ThrowOnSave = true;
        await viewModel.FlushPendingSaveAsync();

        Assert.Equal("draft intacto", viewModel.ContentDraft);
        Assert.Equal(QuickNotesSaveState.Error, viewModel.SaveState);
    }

    [Fact]
    public async Task Autosave_persists_after_the_quiet_period()
    {
        var (viewModel, service, note) = await CreateViewModelWithNoteAsync(autosaveDelay: TimeSpan.FromMilliseconds(20));

        viewModel.ContentDraft = "saved after pause";
        await Task.Delay(100);

        Assert.Equal("saved after pause", service.GetNotes().Single(item => item.Id == note.Id).Content);
    }

    [Fact]
    public async Task Flush_waits_for_an_in_progress_autosave_without_writing_it_twice()
    {
        var store = new BlockingMemoryQuickNotesStore();
        var service = new QuickNotesService(store);
        await service.InitializeAsync();
        var note = Assert.IsType<QuickNote>(await service.SaveDraftAsync(null, Draft("before")));
        var viewModel = new QuickNotesViewModel(service, autosaveDelay: TimeSpan.FromMilliseconds(10));
        store.WriteCalls = 0;
        store.BlockWrites = true;

        viewModel.ContentDraft = "latest";
        await store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task flush = viewModel.FlushPendingSaveAsync();
        store.ReleaseWrite.TrySetResult();
        await flush;

        Assert.Equal(1, store.WriteCalls);
        Assert.Equal("latest", service.GetNotes().Single(item => item.Id == note.Id).Content);
    }

    [Fact]
    public async Task Search_includes_tags_and_content()
    {
        var (viewModel, _, note) = await CreateViewModelWithNoteAsync("receita", ["cozinha"]);

        viewModel.SearchText = "cozinha";
        Assert.Equal(note.Id, Assert.Single(viewModel.Notes).Id);
        viewModel.SearchText = "receita";
        Assert.Equal(note.Id, Assert.Single(viewModel.Notes).Id);
    }

    [Fact]
    public void Settings_view_model_commits_global_hotkey_preference_changes()
    {
        IslandSettings? applied = null;
        var settings = new SettingsViewModel(
            new IslandSettings { QuickNotesHotkeyEnabled = false },
            ["Primary"],
            next => applied = next);

        settings.QuickNotesHotkeyEnabled = true;

        Assert.True(settings.Current.QuickNotesHotkeyEnabled);
        Assert.True(applied!.QuickNotesHotkeyEnabled);
    }

    private static async Task<QuickNotesViewModel> CreateViewModelAsync(TimeSpan? autosaveDelay = null)
    {
        var service = new QuickNotesService(new MemoryQuickNotesStore());
        await service.InitializeAsync();
        return new QuickNotesViewModel(service, autosaveDelay: autosaveDelay);
    }

    private static async Task<(QuickNotesViewModel ViewModel, QuickNotesService Service, QuickNote Current, QuickNote Other)> CreateViewModelWithTwoNotesAsync(
        TimeSpan autosaveDelay)
    {
        var service = new QuickNotesService(new MemoryQuickNotesStore());
        await service.InitializeAsync();
        var other = Assert.IsType<QuickNote>(await service.SaveDraftAsync(null, Draft("other")));
        var current = Assert.IsType<QuickNote>(await service.SaveDraftAsync(null, Draft("current")));
        await service.SetPinnedAsync(current.Id, true);
        return (new QuickNotesViewModel(service, autosaveDelay: autosaveDelay), service, current, other);
    }

    private static async Task<(QuickNotesViewModel ViewModel, MemoryQuickNotesStore Store)> CreateViewModelWithStoreAsync(
        TimeSpan autosaveDelay)
    {
        var store = new MemoryQuickNotesStore();
        var service = new QuickNotesService(store);
        await service.InitializeAsync();
        return (new QuickNotesViewModel(service, autosaveDelay: autosaveDelay), store);
    }

    private static async Task<(QuickNotesViewModel ViewModel, QuickNotesService Service, QuickNote Note)> CreateViewModelWithNoteAsync(
        string content = "texto",
        IReadOnlyList<string>? tags = null,
        TimeSpan? autosaveDelay = null)
    {
        var service = new QuickNotesService(new MemoryQuickNotesStore());
        await service.InitializeAsync();
        var note = Assert.IsType<QuickNote>(await service.SaveDraftAsync(null, Draft(content, tags)));
        return (new QuickNotesViewModel(service, autosaveDelay: autosaveDelay), service, note);
    }

    private static QuickNoteDraft Draft(string content, IReadOnlyList<string>? tags = null) => new(
        string.Empty,
        content,
        Array.Empty<QuickNoteChecklistItem>(),
        tags ?? Array.Empty<string>(),
        QuickNoteColor.Default);

    private sealed class MemoryQuickNotesStore : IQuickNotesStore
    {
        public bool ThrowOnSave { get; set; }

        private IReadOnlyList<QuickNote> _notes = Array.Empty<QuickNote>();

        public Task<IReadOnlyList<QuickNote>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_notes);

        public Task SaveAsync(IReadOnlyList<QuickNote> notes, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave) throw new IOException("The in-memory store rejected the write.");
            _notes = notes.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingMemoryQuickNotesStore : IQuickNotesStore
    {
        private IReadOnlyList<QuickNote> _notes = Array.Empty<QuickNote>();

        public bool BlockWrites { get; set; }
        public int WriteCalls { get; set; }
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<QuickNote>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_notes);

        public async Task SaveAsync(IReadOnlyList<QuickNote> notes, CancellationToken cancellationToken = default)
        {
            WriteCalls++;
            if (BlockWrites)
            {
                WriteStarted.TrySetResult();
                await ReleaseWrite.Task.WaitAsync(cancellationToken);
            }
            _notes = notes.ToArray();
        }
    }
}

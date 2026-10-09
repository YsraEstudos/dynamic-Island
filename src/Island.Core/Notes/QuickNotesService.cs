namespace Island.Core.Notes;

/// <summary>Owns quick-note rules and publishes a new snapshot only after storage confirms a write.</summary>
public sealed class QuickNotesService
{
    private readonly IQuickNotesStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private QuickNote[] _notes = [];
    private bool _initialized;

    public QuickNotesService(IQuickNotesStore store, TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Raised after a persisted mutation. Subscribers may be called from any thread.</summary>
    public event Action? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _initialized)) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            var loaded = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = loaded.Select(CopyNote).ToArray();
            Volatile.Write(ref _notes, snapshot);
            Volatile.Write(ref _initialized, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<QuickNote> GetNotes(
        QuickNoteCollection collection = QuickNoteCollection.Active,
        string? query = null)
    {
        var notes = Volatile.Read(ref _notes);
        var trimmedQuery = query?.Trim();
        IEnumerable<QuickNote> selected = collection switch
        {
            QuickNoteCollection.Active => notes
                .Where(note => note.DeletedAt is null && !note.IsArchived)
                .OrderByDescending(note => note.IsPinned)
                .ThenByDescending(note => note.UpdatedAt),
            QuickNoteCollection.Archived => notes
                .Where(note => note.DeletedAt is null && note.IsArchived)
                .OrderByDescending(note => note.UpdatedAt),
            QuickNoteCollection.Trash => notes
                .Where(note => note.DeletedAt is not null)
                .OrderByDescending(note => note.DeletedAt),
            _ => Enumerable.Empty<QuickNote>()
        };

        if (!string.IsNullOrWhiteSpace(trimmedQuery))
        {
            selected = selected.Where(note =>
                note.Title.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                || note.Content.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase)
                || note.Tags.Any(tag => tag.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase)));
        }

        return Array.AsReadOnly(selected.ToArray());
    }

    public Task<QuickNote?> SaveDraftAsync(
        Guid? id,
        QuickNoteDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return MutateAsync(notes =>
        {
            if (id is null)
            {
                if (!HasContent(draft)) return (notes, (QuickNote?)null);

                var now = _timeProvider.GetUtcNow();
                var note = new QuickNote(
                    Guid.NewGuid(),
                    draft.Title,
                    draft.Content,
                    CopyChecklist(draft.Checklist),
                    NormalizeTags(draft.Tags),
                    draft.Color,
                    false,
                    false,
                    now,
                    now,
                    null);

                return (notes.Append(note).ToArray(), (QuickNote?)note);
            }

            var index = Array.FindIndex(notes, note => note.Id == id.Value && note.DeletedAt is null);
            if (index < 0) return (notes, (QuickNote?)null);

            var updated = notes[index] with
            {
                Title = draft.Title,
                Content = draft.Content,
                Checklist = CopyChecklist(draft.Checklist),
                Tags = NormalizeTags(draft.Tags),
                Color = draft.Color,
                UpdatedAt = _timeProvider.GetUtcNow()
            };
            var next = (QuickNote[])notes.Clone();
            next[index] = updated;
            return (next, (QuickNote?)updated);
        }, cancellationToken);
    }

    /// <summary>One-shot capture used by the quick-capture window: creates a note from plain text. Blank text is ignored.</summary>
    public Task<QuickNote?> CaptureAsync(
        string text,
        QuickNoteColor color = QuickNoteColor.Default,
        bool pinned = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text)) return Task.FromResult<QuickNote?>(null);

        return MutateAsync(notes =>
        {
            var now = _timeProvider.GetUtcNow();
            var note = new QuickNote(
                Guid.NewGuid(),
                string.Empty,
                text.Trim(),
                Array.AsReadOnly(Array.Empty<QuickNoteChecklistItem>()),
                Array.AsReadOnly(Array.Empty<string>()),
                color,
                pinned,
                false,
                now,
                now,
                null);

            return (notes.Append(note).ToArray(), (QuickNote?)note);
        }, cancellationToken);
    }

    /// <summary>Permanently removes everything currently in the trash.</summary>
    public async Task EmptyTrashAsync(CancellationToken cancellationToken = default)
    {
        await MutateAsync(notes =>
        {
            if (!notes.Any(note => note.DeletedAt is not null)) return (notes, false);

            return (notes.Where(note => note.DeletedAt is null).ToArray(), true);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetPinnedAsync(Guid id, bool pinned, CancellationToken cancellationToken = default)
    {
        await MutateAsync(notes =>
        {
            var index = Array.FindIndex(notes, note => note.Id == id && note.DeletedAt is null);
            if (index < 0 || notes[index].IsPinned == pinned) return (notes, false);

            var next = (QuickNote[])notes.Clone();
            next[index] = notes[index] with { IsPinned = pinned, UpdatedAt = _timeProvider.GetUtcNow() };
            return (next, true);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        await MutateAsync(notes =>
        {
            var index = Array.FindIndex(notes, note => note.Id == id && note.DeletedAt is null);
            if (index < 0 || notes[index].IsArchived == archived) return (notes, false);

            var next = (QuickNote[])notes.Clone();
            next[index] = notes[index] with { IsArchived = archived, UpdatedAt = _timeProvider.GetUtcNow() };
            return (next, true);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveToTrashAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await MutateAsync(notes =>
        {
            var index = Array.FindIndex(notes, note => note.Id == id && note.DeletedAt is null);
            if (index < 0) return (notes, false);

            var now = _timeProvider.GetUtcNow();
            var next = (QuickNote[])notes.Clone();
            next[index] = notes[index] with { DeletedAt = now, UpdatedAt = now };
            return (next, true);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreFromTrashAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await MutateAsync(notes =>
        {
            var index = Array.FindIndex(notes, note => note.Id == id && note.DeletedAt is not null);
            if (index < 0) return (notes, false);

            var next = (QuickNote[])notes.Clone();
            next[index] = notes[index] with { DeletedAt = null, UpdatedAt = _timeProvider.GetUtcNow() };
            return (next, true);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeletePermanentlyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await MutateAsync(notes =>
        {
            var index = Array.FindIndex(notes, note => note.Id == id);
            if (index < 0) return (notes, false);

            var next = notes.Where(note => note.Id != id).ToArray();
            return (next, true);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResult> MutateAsync<TResult>(
        Func<QuickNote[], (QuickNote[] Notes, TResult Result)> mutation,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var changed = false;
        TResult result = default!;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Volatile.Read(ref _notes);
            var mutationResult = mutation(current);
            result = mutationResult.Result;

            if (!ReferenceEquals(mutationResult.Notes, current))
            {
                await _store.SaveAsync(mutationResult.Notes, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _notes, mutationResult.Notes);
                changed = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (changed) Changed?.Invoke();
        return result;
    }

    private static bool HasContent(QuickNoteDraft draft) =>
        !string.IsNullOrWhiteSpace(draft.Title)
        || !string.IsNullOrWhiteSpace(draft.Content)
        || draft.Checklist.Any(item => !string.IsNullOrWhiteSpace(item.Text));

    private static IReadOnlyList<QuickNoteChecklistItem> CopyChecklist(IReadOnlyList<QuickNoteChecklistItem> checklist) =>
        Array.AsReadOnly(checklist.ToArray());

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string> tags) => Array.AsReadOnly(
        tags.Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());

    private static QuickNote CopyNote(QuickNote note) => note with
    {
        Checklist = CopyChecklist(note.Checklist),
        Tags = Array.AsReadOnly(note.Tags.ToArray())
    };
}

using Island.Core.Abstractions;

namespace Island.Core.GameNotes;

/// <summary>
/// Owns the game-notes rules and the game the widget shows. A data change publishes a new book only after the store
/// confirms the write; a failed write keeps the previous book and sets <see cref="HasSaveError"/>.
/// <see cref="Changed"/> may be raised from any thread.
/// </summary>
public sealed class GameNotesService : IDisposable
{
    private readonly IGameNotesStore _store;
    private readonly IForegroundGameTracker _tracker;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly GameNotesSelection _selection = new();
    private volatile GameNotesBook _book = new();
    private volatile bool _initialized;
    private volatile bool _hasSaveError;
    private bool _disposed;

    public GameNotesService(IGameNotesStore store, IForegroundGameTracker tracker, TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _timeProvider = timeProvider ?? TimeProvider.System;

        // The tracker may have seen a game before this service subscribed (its hook starts with the app).
        if (_tracker.LastGame is { } last)
        {
            lock (_selection) _selection.ObserveGame(last);
        }
        _tracker.CurrentGameChanged += OnGameDetected;
    }

    /// <summary>Raised after the notes, the shown game or the save state changes. Any thread.</summary>
    public event Action? Changed;

    public bool IsInitialized => _initialized;

    /// <summary>True after a write failed and until the next write succeeds.</summary>
    public bool HasSaveError => _hasSaveError;

    /// <summary>Key of the game the widget shows and the capture hotkey targets, or null when no game is known.</summary>
    public string? ResolvedKey
    {
        get
        {
            lock (_selection) return _selection.ResolvedKey;
        }
    }

    public GameInfo? LastGame
    {
        get
        {
            lock (_selection) return _selection.LastGame;
        }
    }

    /// <summary>Games that have notes, most recently changed first.</summary>
    public IReadOnlyList<GameNotesGame> GamesWithNotes => _book.Games;

    public GameNotesGame? Find(string? key) => _book.Find(key);

    public IReadOnlyList<GameNote> NotesFor(string? key) => _book.NotesFor(key);

    public bool IsPinned(string? key)
    {
        lock (_selection) return key is not null && _selection.PinnedKey == key;
    }

    public string DisplayNameFor(string? key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        GameInfo? last = LastGame;
        if (last is not null && last.Key == key) return last.DisplayName;
        return _book.Find(key)?.DisplayName ?? key;
    }

    public string? ExePathFor(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        GameInfo? last = LastGame;
        if (last is not null && last.Key == key && last.ExePath is not null) return last.ExePath;
        return _book.Find(key)?.ExePath;
    }

    /// <summary>Loads the notes once. Safe to call again; the second call returns at once.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            IReadOnlyList<GameNotesGame> loaded = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            _book = new GameNotesBook(loaded);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke();
    }

    /// <summary>Moves the widget to the next or previous game that has notes (wrapping).</summary>
    public void BrowseStep(int delta)
    {
        lock (_selection)
        {
            var keys = _book.Games.Select(game => game.Key).ToList();
            string? current = _selection.ResolvedKey;
            if (current is not null && !keys.Contains(current)) keys.Insert(0, current);
            _selection.Step(keys, delta);
        }
        Changed?.Invoke();
    }

    /// <summary>Pins the game on screen, or unpins it when it is already pinned.</summary>
    public void TogglePinnedGame()
    {
        lock (_selection) _selection.TogglePinned(_selection.ResolvedKey);
        Changed?.Invoke();
    }

    public Task<bool> AddNoteAsync(string key, string text, CancellationToken cancellationToken = default)
    {
        string? clean = GameNoteText.Clean(text);
        if (clean is null || string.IsNullOrEmpty(key)) return Task.FromResult(false);

        GameInfo game = GameInfoFor(key);
        return MutateAsync(book =>
        {
            var note = new GameNote(Guid.NewGuid(), clean, _timeProvider.GetUtcNow(), false, false);
            return book.WithNote(game, note, note.CreatedAt);
        }, cancellationToken);
    }

    public Task<bool> ToggleNoteAsync(string key, Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(book => book.WithToggledCompleted(key, id, _timeProvider.GetUtcNow()), cancellationToken);

    public Task<bool> SetNotePinnedAsync(string key, Guid id, bool pinned, CancellationToken cancellationToken = default) =>
        MutateAsync(book => book.WithPinned(key, id, pinned, _timeProvider.GetUtcNow()), cancellationToken);

    public Task<bool> RemoveNoteAsync(string key, Guid id, CancellationToken cancellationToken = default) =>
        MutateAsync(book => book.WithoutNote(key, id, _timeProvider.GetUtcNow()), cancellationToken);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tracker.CurrentGameChanged -= OnGameDetected;
    }

    private void OnGameDetected(GameInfo game)
    {
        bool keyChanged;
        lock (_selection) keyChanged = _selection.ObserveGame(game);

        if (keyChanged) Changed?.Invoke();
        // Keeps the display name and executable path of a game that has notes up to date. Writes only when they differ.
        _ = MutateAsync(book => book.WithGameInfo(game), CancellationToken.None);
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the current book and saves it. Returns false when nothing changed or the
    /// save failed. Raises <see cref="Changed"/> when a change was published or a save failed.
    /// </summary>
    private async Task<bool> MutateAsync(Func<GameNotesBook, GameNotesBook?> change, CancellationToken cancellationToken)
    {
        bool published = false;
        bool notify = false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Before a successful load the book is empty: saving it would overwrite the stored notes.
            if (!_initialized) return false;

            GameNotesBook current = _book;
            GameNotesBook? next = change(current);
            if (next is not null && !ReferenceEquals(next, current))
            {
                notify = true;
                try
                {
                    await _store.SaveAsync(next.Games, cancellationToken).ConfigureAwait(false);
                    _book = next;
                    _hasSaveError = false;
                    published = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The previous book stays; the widget shows the error until a later write succeeds.
                    _hasSaveError = true;
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        if (notify) Changed?.Invoke();
        return published;
    }

    /// <summary>The game details to file a note under: the freshest detection, else what was stored, else the key itself.</summary>
    private GameInfo GameInfoFor(string key)
    {
        GameInfo? last = LastGame;
        if (last is not null && last.Key == key) return last;

        GameNotesGame? stored = _book.Find(key);
        if (stored is not null) return new GameInfo(stored.ProcessName, stored.DisplayName, stored.ExePath);
        return new GameInfo(key, key, null);
    }
}

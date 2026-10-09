namespace Island.Core.GameNotes;

/// <summary>A game with its notes, as stored. Immutable: a change makes a new record.</summary>
public sealed record GameNotesGame(
    string Key,
    string ProcessName,
    string DisplayName,
    string? ExePath,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<GameNote> Notes);

/// <summary>
/// Notes grouped by game key. The book is immutable: each change returns a new book, or the same instance when
/// nothing changed, so the service can skip the save.
/// </summary>
public sealed class GameNotesBook
{
    public const int MaxNotesPerGame = 200;
    public const int MaxTextLength = 500;

    private readonly Dictionary<string, GameNotesGame> _games = new(StringComparer.Ordinal);

    public GameNotesBook()
    {
    }

    public GameNotesBook(IEnumerable<GameNotesGame> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        foreach (GameNotesGame game in games)
        {
            if (string.IsNullOrEmpty(game.Key) || game.Notes.Count == 0) continue;
            _games[game.Key] = game with { Notes = OrderNotes(game.Notes) };
        }
    }

    /// <summary>Games that have at least one note, most recently changed first.</summary>
    public IReadOnlyList<GameNotesGame> Games => _games.Values
        .OrderByDescending(game => game.UpdatedAt)
        .ThenBy(game => game.Key, StringComparer.Ordinal)
        .ToArray();

    public GameNotesGame? Find(string? key) =>
        key is not null && _games.TryGetValue(key, out GameNotesGame? game) ? game : null;

    public IReadOnlyList<GameNote> NotesFor(string? key) =>
        Find(key)?.Notes ?? Array.Empty<GameNote>();

    /// <summary>
    /// Adds a note under the game. Returns null when the text is empty or the game is full; the caller
    /// has already cleaned the text with <see cref="GameNoteText.Clean"/>.
    /// </summary>
    public GameNotesBook? WithNote(GameInfo game, GameNote note, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(note);

        string key = game.Key;
        if (key.Length == 0 || string.IsNullOrWhiteSpace(note.Text)) return null;

        GameNotesGame? existing = Find(key);
        if (existing is not null && existing.Notes.Count >= MaxNotesPerGame) return null;

        var notes = existing?.Notes.ToList() ?? new List<GameNote>();
        notes.Add(note);
        var updated = new GameNotesGame(
            key,
            game.ProcessName,
            game.DisplayName,
            game.ExePath ?? existing?.ExePath,
            now,
            OrderNotes(notes));
        return Replace(key, updated);
    }

    /// <summary>Flips the completed flag. Returns this book when the note does not exist.</summary>
    public GameNotesBook WithToggledCompleted(string key, Guid id, DateTimeOffset now) =>
        ChangeNote(key, id, now, note => note with { IsCompleted = !note.IsCompleted });

    /// <summary>Sets the pinned flag. Returns this book when the flag is already set or the note does not exist.</summary>
    public GameNotesBook WithPinned(string key, Guid id, bool pinned, DateTimeOffset now) =>
        ChangeNote(key, id, now, note => note.IsPinned == pinned ? note : note with { IsPinned = pinned });

    /// <summary>Deletes a note. A game with no notes left disappears from the book.</summary>
    public GameNotesBook WithoutNote(string key, Guid id, DateTimeOffset now)
    {
        GameNotesGame? game = Find(key);
        if (game is null || game.Notes.All(note => note.Id != id)) return this;

        var remaining = game.Notes.Where(note => note.Id != id).ToList();
        if (remaining.Count == 0) return Without(key);

        return Replace(key, game with { Notes = OrderNotes(remaining), UpdatedAt = now });
    }

    /// <summary>
    /// Refreshes the display name and executable path of a game that already has notes. Returns this book when
    /// nothing differs, so a plain focus change does not write the file.
    /// </summary>
    public GameNotesBook WithGameInfo(GameInfo game)
    {
        ArgumentNullException.ThrowIfNull(game);

        GameNotesGame? existing = Find(game.Key);
        if (existing is null) return this;
        if (existing.DisplayName == game.DisplayName && existing.ExePath == game.ExePath
            && existing.ProcessName == game.ProcessName) return this;

        return Replace(existing.Key, existing with
        {
            ProcessName = game.ProcessName,
            DisplayName = game.DisplayName,
            ExePath = game.ExePath ?? existing.ExePath,
        });
    }

    /// <summary>Pinned first, then open items, then newest first. Stable for equal keys.</summary>
    public static IReadOnlyList<GameNote> OrderNotes(IEnumerable<GameNote> notes) => notes
        .OrderByDescending(note => note.IsPinned)
        .ThenBy(note => note.IsCompleted)
        .ThenByDescending(note => note.CreatedAt)
        .ToArray();

    private GameNotesBook ChangeNote(string key, Guid id, DateTimeOffset now, Func<GameNote, GameNote> change)
    {
        GameNotesGame? game = Find(key);
        if (game is null) return this;

        int index = -1;
        for (int i = 0; i < game.Notes.Count; i++)
        {
            if (game.Notes[i].Id == id)
            {
                index = i;
                break;
            }
        }
        if (index < 0) return this;

        GameNote current = game.Notes[index];
        GameNote changed = change(current);
        if (ReferenceEquals(changed, current) || changed == current) return this;

        var notes = game.Notes.ToList();
        notes[index] = changed;
        return Replace(key, game with { Notes = OrderNotes(notes), UpdatedAt = now });
    }

    private GameNotesBook Replace(string key, GameNotesGame game)
    {
        var next = new GameNotesBook();
        foreach (KeyValuePair<string, GameNotesGame> pair in _games)
        {
            next._games[pair.Key] = pair.Key == key ? game : pair.Value;
        }
        if (!_games.ContainsKey(key)) next._games[key] = game;
        return next;
    }

    private GameNotesBook Without(string key)
    {
        var next = new GameNotesBook();
        foreach (KeyValuePair<string, GameNotesGame> pair in _games)
        {
            if (pair.Key != key) next._games[pair.Key] = pair.Value;
        }
        return next;
    }
}

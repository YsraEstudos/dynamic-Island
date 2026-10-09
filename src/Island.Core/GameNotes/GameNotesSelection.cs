namespace Island.Core.GameNotes;

/// <summary>
/// Decides which game the widget (and the capture hotkey) works on. Order: a pinned game wins, then a game the user
/// browsed to with the arrows, then the last game detected in the foreground. Not thread-safe; the service locks it.
/// </summary>
public sealed class GameNotesSelection
{
    public string? PinnedKey { get; private set; }

    public string? BrowsedKey { get; private set; }

    public GameInfo? LastGame { get; private set; }

    /// <summary>Key of the game being shown, or null when no game is known yet.</summary>
    public string? ResolvedKey => PinnedKey ?? BrowsedKey ?? LastGame?.Key;

    /// <summary>
    /// Records a game detected in the foreground. A different game ends any browsing, because the user is now in
    /// another game. A pin is kept until the user unpins it. Returns true when the game changed.
    /// </summary>
    public bool ObserveGame(GameInfo game)
    {
        ArgumentNullException.ThrowIfNull(game);

        bool changed = LastGame?.Key != game.Key;
        LastGame = game;
        if (changed) BrowsedKey = null;
        return changed;
    }

    /// <summary>Shows another game for the arrows. Null clears the browsing choice.</summary>
    public void Browse(string? key) => BrowsedKey = string.IsNullOrEmpty(key) ? null : key;

    /// <summary>Moves through the games in <paramref name="keys"/> (wrapping) from the one on screen. Returns the new key.</summary>
    public string? Step(IReadOnlyList<string> keys, int delta)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0) return null;

        string? current = ResolvedKey;
        int index = current is null ? -1 : IndexOf(keys, current);
        int next = index < 0
            ? (delta >= 0 ? 0 : keys.Count - 1)
            : ((index + delta) % keys.Count + keys.Count) % keys.Count;

        Browse(keys[next]);
        // A pinned game would hide the browsed one, so the arrows move the pin along with it.
        if (PinnedKey is not null) PinnedKey = keys[next];
        return keys[next];
    }

    /// <summary>Pins <paramref name="key"/>, or unpins it when it is already the pinned game. Returns true when pinned.</summary>
    public bool TogglePinned(string? key)
    {
        if (string.IsNullOrEmpty(key)) return PinnedKey is not null;

        PinnedKey = PinnedKey == key ? null : key;
        return PinnedKey is not null;
    }

    private static int IndexOf(IReadOnlyList<string> keys, string key)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i] == key) return i;
        }
        return -1;
    }
}

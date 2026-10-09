using Island.Core.GameNotes;

namespace Island.Core.Abstractions;

/// <summary>Tracks the game in the foreground, event-driven (no polling). Implementations remember the last game.</summary>
public interface IForegroundGameTracker : IDisposable
{
    /// <summary>
    /// Raised each time a game comes to the foreground, including when it is already the last game (so a changed
    /// display name or executable path reaches subscribers). Raised on the UI thread in the Windows adapter.
    /// </summary>
    event Action<GameInfo>? CurrentGameChanged;

    /// <summary>The last game seen in the foreground. Kept when another app takes focus. Null until a game is seen.</summary>
    GameInfo? LastGame { get; }

    /// <summary>Installs the hooks and reads the current foreground window. Idempotent; call on the UI thread.</summary>
    void Start();
}

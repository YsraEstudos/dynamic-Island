namespace Island.App.Widgets;

/// <summary>UI-independent entry points for the game-notes capture window.</summary>
public interface IGameNotesWindowHost
{
    event Action<bool>? HotkeyConflictChanged;

    /// <summary>True when Ctrl+Alt+G is taken by another app.</summary>
    bool HotkeyConflict { get; }

    /// <summary>Opens the capture window for <paramref name="gameKey"/>, or for the game on screen when null.</summary>
    void OpenForCapture(string? gameKey = null);

    void SetHotkeyConflict(bool conflict);
}

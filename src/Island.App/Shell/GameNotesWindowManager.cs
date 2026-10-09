using Island.App.Widgets;
using Island.Core.GameNotes;

namespace Island.App.Shell;

/// <summary>
/// Keeps one game-notes capture window alive. Typing needs an activatable window: the island is WS_EX_NOACTIVATE,
/// so the capture window (the same approach as the quick notes) takes the keyboard.
/// </summary>
public sealed class GameNotesWindowManager : IGameNotesWindowHost
{
    private readonly GameNotesService _service;
    private readonly Func<bool> _reduceAnimations;
    private GameNotesWindow? _window;

    public GameNotesWindowManager(GameNotesService service, Func<bool> reduceAnimations)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _reduceAnimations = reduceAnimations ?? throw new ArgumentNullException(nameof(reduceAnimations));
    }

    public event Action<bool>? HotkeyConflictChanged;

    public bool HotkeyConflict { get; private set; }

    public void OpenForCapture(string? gameKey = null)
    {
        try
        {
            string? key = gameKey ?? _service.ResolvedKey;
            GameNotesWindow window = GetOrCreateWindow();
            window.ShowForGame(key, _service.DisplayNameFor(key));
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not open game notes for capture");
        }
    }

    public void SetHotkeyConflict(bool conflict)
    {
        if (HotkeyConflict == conflict) return;

        HotkeyConflict = conflict;
        HotkeyConflictChanged?.Invoke(conflict);
    }

    public void CloseForShutdown()
    {
        GameNotesWindow? window = _window;
        _window = null;
        window?.CloseForShutdown();
    }

    private GameNotesWindow GetOrCreateWindow()
    {
        if (_window is { } existing) return existing;

        var window = new GameNotesWindow(_service, _reduceAnimations);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window)) _window = null;
        };
        _window = window;
        return window;
    }
}

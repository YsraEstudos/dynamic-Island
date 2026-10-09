using System.Windows.Threading;
using Island.App.Widgets;
using Island.Windows.Input;
using Serilog;

namespace Island.App.Shell;

/// <summary>
/// Owns the Ctrl+Alt+G registration that opens the game-notes capture window. The hotkey fires on its own thread,
/// so the request is marshalled to the UI thread. Registration is made once at startup; a conflict is reported to
/// the widget, which then points the user to the + button.
/// </summary>
public sealed class GameNotesHotkeyController : IDisposable
{
    private const uint VirtualKeyG = 0x47;

    private readonly IGameNotesWindowHost _host;
    private readonly Dispatcher _dispatcher;
    private IGlobalHotkey? _hotkey;
    private bool _started;
    private bool _disposed;

    public GameNotesHotkeyController(IGameNotesWindowHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        // Constructed by the container during startup, which runs on the UI thread.
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <summary>Registers Ctrl+Alt+G. Idempotent. Never throws: a failure becomes a conflict.</summary>
    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;

        try
        {
            var hotkey = new GlobalHotkey(GlobalHotkey.ModControl | GlobalHotkey.ModAlt, VirtualKeyG);
            hotkey.Pressed += OnPressed;
            _hotkey = hotkey;
            bool registered = hotkey.Register();
            if (!registered)
            {
                ReleaseHotkey();
                Log.Warning("Ctrl+Alt+G is already taken; game notes hotkey is unavailable");
            }
            _host.SetHotkeyConflict(!registered);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Game notes hotkey could not be registered");
            ReleaseHotkey();
            _host.SetHotkeyConflict(true);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseHotkey();
    }

    private void OnPressed() => _dispatcher.BeginInvoke(new Action(() => _host.OpenForCapture()));

    private void ReleaseHotkey()
    {
        if (_hotkey is null) return;
        _hotkey.Pressed -= OnPressed;
        _hotkey.Dispose();
        _hotkey = null;
    }
}

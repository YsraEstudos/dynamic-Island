using Island.Core.Abstractions;
using Island.Core.GameNotes;
using Island.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Island.Windows.GameNotes;

/// <summary>
/// Tracks the game in the foreground with one WinEvent hook (EVENT_SYSTEM_FOREGROUND). Event-driven: there is no
/// timer and nothing is polled. Only the foreground window's process name, executable path, file description and
/// geometry are read, never process memory.
/// <para>
/// <see cref="Start"/> must run on the UI thread, which pumps the messages that carry the out-of-context callbacks.
/// </para>
/// </summary>
public sealed class WinEventForegroundGameTracker : IForegroundGameTracker
{
    private readonly Func<IReadOnlyList<string>> _configuredGames;
    private readonly ILogger<WinEventForegroundGameTracker>? _logger;
    private readonly ForegroundProcessReader _reader = new();
    // Rooted here for the lifetime of the instance; the OS keeps only a raw function pointer.
    private readonly NativeMethods.WinEventDelegate _winEventProc;
    private IntPtr _hook;
    private bool _started;
    private bool _disposed;
    private GameInfo? _lastGame;

    /// <param name="configuredGames">
    /// Returns the process names listed in the game settings. Read on every focus change, so edits apply from the next change.
    /// </param>
    public WinEventForegroundGameTracker(Func<IReadOnlyList<string>> configuredGames, ILogger<WinEventForegroundGameTracker>? logger = null)
    {
        _configuredGames = configuredGames ?? throw new ArgumentNullException(nameof(configuredGames));
        _logger = logger;
        _winEventProc = OnWinEvent;
    }

    public event Action<GameInfo>? CurrentGameChanged;

    public GameInfo? LastGame => _lastGame;

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;

        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        if (_hook == IntPtr.Zero)
            _logger?.LogWarning("Could not install the foreground WinEvent hook; game notes will not follow the foreground game.");

        // The game may already be in front when the app starts.
        Evaluate(NativeMethods.GetForegroundWindow());
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Exceptions must not escape into the native callback.
        try
        {
            if (_disposed || eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND) return;
            Evaluate(hwnd);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Foreground game check failed.");
        }
    }

    private void Evaluate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        ForegroundProcess process = _reader.Read(hwnd);
        GameInfo? game = GameDetectionRules.Detect(process, ConfiguredGames());
        if (game is null) return;   // a windowed app or the island itself: the last game stays

        _lastGame = game;
        CurrentGameChanged?.Invoke(game);
    }

    private IReadOnlyList<string> ConfiguredGames()
    {
        try
        {
            return _configuredGames() ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not read the game process list.");
            return Array.Empty<string>();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}

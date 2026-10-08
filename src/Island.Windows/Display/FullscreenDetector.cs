using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Island.Core.Abstractions;
using Island.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Display;

/// <summary>
/// Reports whether the foreground window covers its whole monitor (a fullscreen game or video).
/// Event-driven through WinEvent hooks; there is no polling timer.
/// <para>
/// <see cref="Start"/> must run on a thread that pumps window messages (the WPF UI thread).
/// Out-of-context WinEvent callbacks are delivered through that thread's message queue.
/// </para>
/// </summary>
public sealed class FullscreenDetector : IDisplayService
{
    private static readonly HashSet<string> ShellClassNames = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    private static readonly Func<IReadOnlyList<string>> NoGameProcesses = () => Array.Empty<string>();

    private readonly ILogger<FullscreenDetector>? _logger;
    private readonly Func<IReadOnlyList<string>> _gameProcesses;
    private readonly object _gate = new();
    // Rooted here for the lifetime of the instance; the OS keeps only a raw function pointer.
    private readonly NativeMethods.WinEventDelegate _winEventProc;
    private IntPtr _foregroundHook;
    private IntPtr _locationHook;
    private uint _locationHookPid;
    private bool _started;
    private bool _disposed;
    private volatile bool _isFullscreen;

    /// <param name="gameProcesses">
    /// Returns the configured game process names. Read on every foreground change, so edits apply from the next change.
    /// </param>
    public FullscreenDetector(ILogger<FullscreenDetector>? logger = null,
        Func<IReadOnlyList<string>>? gameProcesses = null)
    {
        _logger = logger;
        _gameProcesses = gameProcesses ?? NoGameProcesses;
        _winEventProc = OnWinEvent;
    }

    public event EventHandler<bool>? FullscreenChanged;

    public bool IsFullscreenAppActive => _isFullscreen;

    /// <summary>Installs the foreground hook and the per-process location hook. Idempotent.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
            _foregroundHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
            if (_foregroundHook == IntPtr.Zero)
                _logger?.LogWarning("Could not install foreground WinEvent hook; fullscreen detection disabled.");
            RetargetLocationHookLocked();
        }
        Evaluate();
    }

    /// <summary>
    /// Recomputes fullscreen state for the current foreground window and raises
    /// <see cref="FullscreenChanged"/> only when it actually changed. Safe to call without <see cref="Start"/>.
    /// </summary>
    public bool Evaluate()
    {
        if (_disposed) return _isFullscreen;

        bool fullscreen;
        try
        {
            fullscreen = IsForegroundFullscreen();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Fullscreen evaluation failed; treating as not fullscreen.");
            fullscreen = false;
        }

        bool changed;
        lock (_gate)
        {
            // Disposed while the check ran: keep the state, but raise nothing.
            changed = !_disposed && fullscreen != _isFullscreen;
            _isFullscreen = fullscreen;
        }

        if (changed) RaiseFullscreenChanged(fullscreen);
        return fullscreen;
    }

    private bool IsForegroundFullscreen()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return false;   // our own overlay/settings windows

        if (IsShellWindow(hwnd)) return false;
        if (NativeMethods.IsIconic(hwnd)) return false;

        // Listed games count even in windowed mode, so this runs before the geometry test.
        if (IsListedGameForeground(pid)) return true;

        // The shell reports D3D/exclusive fullscreen even when the window rect does not cover the monitor.
        if (IsFullscreenNotificationState()) return true;

        // A maximized window with auto-hidden taskbar also covers the monitor; it is not a fullscreen app.
        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
        if ((style & NativeMethods.WS_MAXIMIZE) != 0) return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect)) return false;

        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfoW(monitor, ref info)) return false;

        // Compare against the full monitor rect, not the work area, so the taskbar does not hide a fullscreen app.
        var m = info.rcMonitor;
        return windowRect.Left <= m.Left && windowRect.Top <= m.Top
            && windowRect.Right >= m.Right && windowRect.Bottom >= m.Bottom;
    }

    private static bool IsShellWindow(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        int len = NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return len > 0 && ShellClassNames.Contains(sb.ToString());
    }

    /// <summary>
    /// True when the foreground process is one of the configured games. Only the process name is compared;
    /// the window geometry is irrelevant.
    /// </summary>
    private bool IsListedGameForeground(uint pid)
    {
        IReadOnlyList<string>? games;
        try
        {
            games = _gameProcesses();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not read the game process list.");
            return false;
        }

        if (games is null || games.Count == 0) return false;   // no process lookup when nothing is configured

        string? processName = GetProcessName(pid);
        return processName is not null && MatchesGameList(processName, games);
    }

    private string? GetProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex)
        {
            // Process exited, or access denied (e.g. an elevated game while Island runs unelevated).
            _logger?.LogDebug(ex, "Could not read the foreground process name.");
            return null;
        }
    }

    /// <summary>
    /// True when the shell reports an exclusive Direct3D fullscreen app (a game). QUNS_BUSY and QUNS_PRESENTATION_MODE
    /// are not counted: they are system-wide, so they would hide the island for a fullscreen app on another monitor.
    /// Any failure yields false.
    /// </summary>
    private bool IsFullscreenNotificationState()
    {
        try
        {
            int hr = NativeMethods.SHQueryUserNotificationState(out int state);
            if (hr < 0) return false;   // FAILED(hr)
            return state == NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "SHQueryUserNotificationState failed; ignoring it.");
            return false;
        }
    }

    /// <summary>
    /// Case-insensitive exact match of a process name against the configured list. The ".exe" suffix and
    /// surrounding spaces are ignored on both sides. An empty list never matches.
    /// </summary>
    internal static bool MatchesGameList(string processName, IReadOnlyList<string> games)
    {
        if (string.IsNullOrWhiteSpace(processName) || games is null || games.Count == 0) return false;

        string target = NormalizeProcessName(processName);
        foreach (string game in games)
        {
            if (game is null) continue;
            if (NormalizeProcessName(game).Equals(target, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string NormalizeProcessName(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4].TrimEnd();
        return trimmed;
    }

    /// <summary>
    /// Keeps the location hook scoped to the foreground window's process. A global location hook would
    /// receive events from every window on the system.
    /// </summary>
    private void RetargetLocationHookLocked()
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        uint pid = 0;
        if (fg != IntPtr.Zero) NativeMethods.GetWindowThreadProcessId(fg, out pid);

        if (pid == _locationHookPid && _locationHook != IntPtr.Zero) return;

        if (_locationHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_locationHook);
            _locationHook = IntPtr.Zero;
        }
        _locationHookPid = pid;
        if (pid == 0) return;

        _locationHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _winEventProc, pid, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Exceptions must not escape into the native callback.
        try
        {
            if (_disposed) return;

            if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
            {
                lock (_gate) RetargetLocationHookLocked();
                Evaluate();
            }
            else if (eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE)
            {
                if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF) return;
                if (hwnd != NativeMethods.GetForegroundWindow()) return;
                Evaluate();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "WinEvent callback failed.");
        }
    }

    private void RaiseFullscreenChanged(bool fullscreen)
    {
        try
        {
            FullscreenChanged?.Invoke(this, fullscreen);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "FullscreenChanged handler threw.");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_foregroundHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_foregroundHook);
            if (_locationHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_locationHook);
            _foregroundHook = IntPtr.Zero;
            _locationHook = IntPtr.Zero;
        }
    }
}

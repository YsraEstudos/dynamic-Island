using Island.Windows.Focus;
using Island.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Guardian;

/// <summary>
/// The title guard of the guardian process: it minimizes a browser window whose caption names a blocked site, using the
/// same <see cref="BlockedSites"/> rules and <see cref="ForegroundSiteGuard.ShouldBlock(string?, string?, SiteGuardMode, out string?)"/>
/// decision as the island, but without UI Automation: only the native window title is read, so it works with the island
/// dead and adds no dependency on the accessibility stack.
/// <para>
/// Hooks are installed on a dedicated message-loop thread (<see cref="MessageWindowThread"/>) because
/// <c>WINEVENT_OUTOFCONTEXT</c> callbacks are delivered through the installing thread's message queue. The GUI process
/// is hidden and never creates a window of its own.
/// </para>
/// </summary>
public sealed class GuardianTitleGuard : IDisposable
{
    private readonly ILogger? _log;
    private readonly MessageWindowThread _thread;
    // Rooted here for the lifetime of the instance; the OS keeps only a raw function pointer.
    private readonly NativeMethods.WinEventDelegate _winEventProc;
    private readonly object _gate = new();
    private IntPtr _foregroundHook;
    private IntPtr _nameChangeHook;
    private volatile SiteGuardMode _mode;
    private bool _started;
    private bool _disposed;

    public GuardianTitleGuard(ILogger? log = null)
    {
        _log = log;
        _thread = new MessageWindowThread("GuardianTitles", onMessage: null, log);
        _winEventProc = OnWinEvent;
    }

    /// <summary>
    /// Sets what is blocked. <see cref="SiteGuardMode.Off"/> removes the hooks; any other mode installs them and checks
    /// the window already in front (the user may be on a blocked site when the guard starts). Idempotent and thread-safe.
    /// </summary>
    public void SetMode(SiteGuardMode mode)
    {
        if (_disposed) return;

        if (!_started)
        {
            if (mode == SiteGuardMode.Off) return;
            if (!_thread.Start())
            {
                _log?.LogWarning("Guardian title guard thread did not start; only the island relaunch will run.");
                return;
            }
            _started = true;
        }

        bool install;
        bool remove;
        lock (_gate)
        {
            if (_disposed || _mode == mode) return;
            bool wasOff = _mode == SiteGuardMode.Off;
            _mode = mode;
            install = wasOff && mode != SiteGuardMode.Off;
            remove = !wasOff && mode == SiteGuardMode.Off;
        }

        if (install)
        {
            if (!_thread.Invoke(InstallHooks))
                _log?.LogWarning("Guardian could not install its browser title hooks; blocked sites may stay open.");
            // The user may be on a blocked page at the exact moment the island dies.
            EvaluateForeground();
        }
        else if (remove)
        {
            _thread.Invoke(RemoveHooks);
        }
    }

    /// <summary>Checks the window in front now. Safe from any thread; used once per guardian poll to cover missed events.</summary>
    public void EvaluateForeground()
    {
        if (_mode == SiteGuardMode.Off) return;
        try
        {
            Evaluate(NativeMethods.GetForegroundWindow());
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Guardian browser title check failed");
        }
    }

    private bool InstallHooks()
    {
        if (_foregroundHook != IntPtr.Zero || _nameChangeHook != IntPtr.Zero) return true;
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        // Tab switches change the browser caption without changing the foreground window.
        _nameChangeHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_NAMECHANGE, NativeMethods.EVENT_OBJECT_NAMECHANGE,
            IntPtr.Zero, _winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        return _foregroundHook != IntPtr.Zero && _nameChangeHook != IntPtr.Zero;
    }

    private bool RemoveHooks()
    {
        if (_foregroundHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_foregroundHook);
        if (_nameChangeHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_nameChangeHook);
        _foregroundHook = IntPtr.Zero;
        _nameChangeHook = IntPtr.Zero;
        return true;
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        // Native callback: nothing may escape from here.
        try
        {
            if (_mode == SiteGuardMode.Off || hwnd == IntPtr.Zero) return;

            if (eventType == NativeMethods.EVENT_OBJECT_NAMECHANGE)
            {
                // Without UI Automation only the window caption is read, so client-object events are of no use.
                if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF) return;
                if (hwnd != NativeMethods.GetForegroundWindow()) return;
            }

            Evaluate(hwnd);
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Guardian browser title check failed");
        }
    }

    private void Evaluate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        string title = NativeMethods.GetWindowTitle(hwnd);
        string? processName = TryGetProcessName(hwnd);
        string windowClass = NativeMethods.GetWindowClass(hwnd);
        if (!ForegroundSiteGuard.ShouldBlockWithWindowClass(processName, windowClass, title, _mode, out string? siteName)) return;

        NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_MINIMIZE);
        _log?.LogInformation("Guardian minimized a {Site} browser window while the island was down", siteName);
    }

    /// <summary>Process name without ".exe", or null when the window belongs to this process or cannot be read.</summary>
    private static string? TryGetProcessName(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return null;

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _mode = SiteGuardMode.Off;
        }

        if (_started)
        {
            _thread.Invoke(RemoveHooks);
            _thread.Dispose();
        }
    }
}

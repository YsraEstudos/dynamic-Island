using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Island.Core.Pomodoro;
using Island.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Focus;

/// <summary>
/// Minimizes a browser window whose window or accessible client title names a blocked site during angry mode.
/// Event-driven through WinEvent hooks; there is no polling timer. The client title survives named Edge windows.
/// <para>
/// <see cref="SetActive"/> must run on a thread that pumps window messages (the WPF UI thread).
/// Out-of-context WinEvent callbacks are delivered through that thread's message queue.
/// </para>
/// </summary>
public sealed class ForegroundSiteGuard : IDisposable
{
    private static readonly TimeSpan ReopenedBrowserGracePeriod = TimeSpan.FromSeconds(3);

    private readonly ILogger<ForegroundSiteGuard>? _logger;
    private readonly BrowserTitleReader _browserTitles;
    private readonly object _gate = new();
    private readonly Dictionary<IntPtr, CancellationTokenSource> _foregroundGracePeriods = new();
    // Rooted here for the lifetime of the instance; the OS keeps only a raw function pointer.
    private readonly NativeMethods.WinEventDelegate _winEventProc;
    private IntPtr _foregroundHook;
    private IntPtr _nameChangeHook;
    private volatile bool _active;
    private bool _disposed;

    public ForegroundSiteGuard(ILogger<ForegroundSiteGuard>? logger = null)
    {
        _logger = logger;
        _browserTitles = new BrowserTitleReader(NativeMethods.GetBrowserClientTitle, OnBrowserTitleRead, logger);
        _winEventProc = OnWinEvent;
    }

    /// <summary>Raised after a blocked window was minimized, on the thread that owns the hooks (the UI thread).</summary>
    public event Action<string>? SiteBlocked;

    public bool Active => _active;

    /// <summary>
    /// Installs or removes the hooks. Turning it on also checks the window already in front, because the user
    /// may be on a blocked site when the mode starts. Idempotent.
    /// </summary>
    public void SetActive(bool active)
    {
        lock (_gate)
        {
            if (_disposed || _active == active) return;
            _active = active;
            if (active) InstallHooksLocked();
            else RemoveHooksLocked();
        }

        // Outside the lock: a SiteBlocked subscriber may call back into SetActive.
        if (active) EvaluateWindow(NativeMethods.GetForegroundWindow());
    }

    /// <summary>
    /// The single block decision, kept pure so the rule can be tested without hooks. Only browsers are
    /// considered, so a Notepad file titled "YouTube notes" is not blocked.
    /// </summary>
    public static bool ShouldBlock(string? processName, string? windowTitle,
        [NotNullWhen(true)] out string? siteName)
    {
        siteName = BlockedSites.IsBrowserProcess(processName) ? BlockedSites.Match(windowTitle) : null;
        return siteName is not null;
    }

    /// <summary>The accessible browser client names the active page even when the window was renamed.</summary>
    public static bool ShouldBlock(string? processName, string? windowTitle, string? browserTitle,
        [NotNullWhen(true)] out string? siteName)
    {
        siteName = BlockedSites.IsBrowserProcess(processName)
            ? BlockedSites.Match(windowTitle) ?? BlockedSites.Match(browserTitle)
            : null;
        return siteName is not null;
    }

    internal static bool IsBrowserTitleChange(int objectId, int childId) =>
        (objectId == NativeMethods.OBJID_WINDOW && childId == NativeMethods.CHILDID_SELF)
        || objectId == NativeMethods.OBJID_CLIENT;

    private void InstallHooksLocked()
    {
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        // Tab switches change the browser's title without changing the foreground window.
        _nameChangeHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_NAMECHANGE, NativeMethods.EVENT_OBJECT_NAMECHANGE,
            IntPtr.Zero, _winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        if (_foregroundHook == IntPtr.Zero || _nameChangeHook == IntPtr.Zero)
            _logger?.LogWarning("Could not install all browser site hooks; blocked sites may stay open.");
    }

    private void RemoveHooksLocked()
    {
        _browserTitles.Cancel();
        foreach (var cancellation in _foregroundGracePeriods.Values) cancellation.Cancel();
        _foregroundGracePeriods.Clear();
        if (_foregroundHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_foregroundHook);
        if (_nameChangeHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_nameChangeHook);
        _foregroundHook = IntPtr.Zero;
        _nameChangeHook = IntPtr.Zero;
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Exceptions must not escape into the native callback.
        try
        {
            // Events queued before the hooks were removed can still arrive; ignore them.
            if (!_active) return;

            if (eventType == NativeMethods.EVENT_OBJECT_NAMECHANGE)
            {
                // Named windows keep their caption on tab switches. Chromium instead changes accessible client
                // names, using nonzero (usually negative) child IDs. Always read the client root, never page text.
                if (!IsBrowserTitleChange(idObject, idChild)) return;
                if (hwnd != NativeMethods.GetForegroundWindow()) return;
            }

            EvaluateWindow(hwnd, foregroundChanged: eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Browser site check failed.");
        }
    }

    private void EvaluateWindow(IntPtr hwnd, bool foregroundChanged = false)
    {
        if (hwnd == IntPtr.Zero) return;

        string title = NativeMethods.GetWindowTitle(hwnd);
        string? processName = TryGetProcessName(hwnd);
        if (!BlockedSites.IsBrowserProcess(processName)) return;

        if (foregroundChanged) StartForegroundGracePeriod(hwnd);
        if (IsInForegroundGracePeriod(hwnd)) return;

        if (ShouldBlock(processName, title, out string? siteName)) MinimizeWindow(hwnd, siteName);
        else _browserTitles.Queue(hwnd, SynchronizationContext.Current);
    }

    private void OnBrowserTitleRead(IntPtr hwnd, string? browserTitle)
    {
        if (!_active || hwnd != NativeMethods.GetForegroundWindow() || IsInForegroundGracePeriod(hwnd)) return;
        if (ShouldBlock(TryGetProcessName(hwnd), NativeMethods.GetWindowTitle(hwnd), browserTitle, out string? siteName))
            MinimizeWindow(hwnd, siteName);
    }

    private void StartForegroundGracePeriod(IntPtr hwnd)
    {
        var cancellation = new CancellationTokenSource();
        var context = SynchronizationContext.Current;
        lock (_gate)
        {
            if (_disposed || !_active)
            {
                cancellation.Dispose();
                return;
            }

            if (_foregroundGracePeriods.Remove(hwnd, out var previous)) previous.Cancel();
            _foregroundGracePeriods[hwnd] = cancellation;
        }

        _ = ReevaluateAfterForegroundGracePeriodAsync(hwnd, cancellation, context);
    }

    private bool IsInForegroundGracePeriod(IntPtr hwnd)
    {
        lock (_gate) return _foregroundGracePeriods.ContainsKey(hwnd);
    }

    private async Task ReevaluateAfterForegroundGracePeriodAsync(IntPtr hwnd,
        CancellationTokenSource cancellation, SynchronizationContext? context)
    {
        bool cancellationDisposedByCallback = false;
        try
        {
            await Task.Delay(ReopenedBrowserGracePeriod, cancellation.Token).ConfigureAwait(false);

            void Reevaluate()
            {
                try
                {
                    lock (_gate)
                    {
                        if (_disposed || !_active
                            || !_foregroundGracePeriods.TryGetValue(hwnd, out var current)
                            || !ReferenceEquals(current, cancellation)) return;
                        _foregroundGracePeriods.Remove(hwnd);
                    }

                    if (hwnd == NativeMethods.GetForegroundWindow()) EvaluateWindow(hwnd);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Browser site check failed after its reopen grace period.");
                }
            }

            if (context is null) Reevaluate();
            else
            {
                context.Post(_ =>
                {
                    try { Reevaluate(); }
                    finally { cancellation.Dispose(); }
                }, null);
                cancellationDisposedByCallback = true;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer foreground event or disabling Angry superseded this grace period.
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Browser site check could not resume after its reopen grace period.");
        }
        finally
        {
            if (!cancellationDisposedByCallback) cancellation.Dispose();
        }
    }

    private void MinimizeWindow(IntPtr hwnd, string siteName)
    {
        // The browser can switch windows while accessibility is being read. Do not minimize a background window.
        if (!_active || hwnd != NativeMethods.GetForegroundWindow()) return;

        NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SW_MINIMIZE);
        _logger?.LogInformation("Minimized a {Site} browser window during angry mode.", siteName);
        RaiseSiteBlocked(siteName);
    }

    /// <summary>
    /// Process name without ".exe", or null when the window belongs to this app or the process cannot be read
    /// (access denied, or it already exited). Null means "no block", so a failed lookup never blocks.
    /// </summary>
    private static string? TryGetProcessName(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return null;

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void RaiseSiteBlocked(string siteName)
    {
        try
        {
            SiteBlocked?.Invoke(siteName);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SiteBlocked handler threw.");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _active = false;
            RemoveHooksLocked();
        }
    }
}

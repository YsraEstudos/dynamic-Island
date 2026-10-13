using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Island.Core.Pomodoro;
using Island.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Focus;

/// <summary>What the <see cref="ForegroundSiteGuard"/> blocks while it is on.</summary>
public enum SiteGuardMode
{
    /// <summary>Nothing is blocked and no hooks are installed.</summary>
    Off,
    /// <summary>Only adult sites are blocked (<see cref="BlockedSite.IsAdult"/>).</summary>
    AdultOnly,
    /// <summary>Every site in <see cref="BlockedSites"/> is blocked.</summary>
    All,
}

/// <summary>
/// Minimizes a browser window whose window or accessible client title names a blocked site, in the mode set by
/// <see cref="SetMode"/>. Event-driven through WinEvent hooks; there is no polling timer. The client title survives named Edge windows.
/// <para>
/// <see cref="SetMode"/> and <see cref="SetActive"/> must run on a thread that pumps window messages (the WPF UI thread).
/// Out-of-context WinEvent callbacks are delivered through that thread's message queue.
/// The mode is volatile because the callbacks read it without taking the lock.
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
    private volatile SiteGuardMode _mode;
    private bool _disposed;

    public ForegroundSiteGuard(ILogger<ForegroundSiteGuard>? logger = null)
    {
        _logger = logger;
        _browserTitles = new BrowserTitleReader(NativeMethods.GetBrowserClientTitle, OnBrowserTitleRead, logger);
        _winEventProc = OnWinEvent;
    }

    /// <summary>Raised after a blocked window was minimized, on the thread that owns the hooks (the UI thread).</summary>
    public event Action<string>? SiteBlocked;

    /// <summary>True in any mode other than <see cref="SiteGuardMode.Off"/>.</summary>
    public bool Active => _mode != SiteGuardMode.Off;

    /// <summary>The current mode.</summary>
    public SiteGuardMode Mode => _mode;

    /// <summary>Same as <see cref="SetMode"/>: true blocks every site, false turns the guard off.</summary>
    public void SetActive(bool active) => SetMode(active ? SiteGuardMode.All : SiteGuardMode.Off);

    /// <summary>
    /// Sets what is blocked. <see cref="SiteGuardMode.Off"/> removes the hooks. Any other mode installs them and checks
    /// the window already in front, because the user may be on a blocked site when the mode starts. Switching between
    /// AdultOnly and All keeps the hooks and checks the foreground window again. Idempotent.
    /// </summary>
    public void SetMode(SiteGuardMode mode)
    {
        bool check;
        lock (_gate)
        {
            if (_disposed || _mode == mode) return;
            bool wasOff = _mode == SiteGuardMode.Off;
            _mode = mode;
            if (mode == SiteGuardMode.Off) RemoveHooksLocked();
            else if (wasOff) InstallHooksLocked();
            check = mode != SiteGuardMode.Off;
        }

        // Outside the lock: a SiteBlocked subscriber may call back into SetMode.
        if (check) EvaluateWindow(NativeMethods.GetForegroundWindow());
    }

    /// <summary>
    /// The single block decision, kept pure so the rule can be tested without hooks. Only browsers are
    /// considered, so a Notepad file titled "YouTube notes" is not blocked.
    /// </summary>
    public static bool ShouldBlock(string? processName, string? windowTitle,
        [NotNullWhen(true)] out string? siteName) =>
        ShouldBlock(processName, windowTitle, false, out siteName);

    /// <summary><see cref="ShouldBlock(string?, string?, out string?)"/>, limited to adult sites when <paramref name="adultOnly"/> is true.</summary>
    public static bool ShouldBlock(string? processName, string? windowTitle, bool adultOnly,
        [NotNullWhen(true)] out string? siteName)
    {
        siteName = BlockedSites.IsBrowserProcess(processName) ? BlockedSites.Match(windowTitle, adultOnly) : null;
        return siteName is not null;
    }

    /// <summary>The accessible browser client names the active page even when the window was renamed.</summary>
    public static bool ShouldBlock(string? processName, string? windowTitle, string? browserTitle,
        [NotNullWhen(true)] out string? siteName) =>
        ShouldBlock(processName, windowTitle, browserTitle, false, out siteName);

    /// <summary><see cref="ShouldBlock(string?, string?, string?, out string?)"/>, limited to adult sites when <paramref name="adultOnly"/> is true.</summary>
    public static bool ShouldBlock(string? processName, string? windowTitle, string? browserTitle, bool adultOnly,
        [NotNullWhen(true)] out string? siteName)
    {
        siteName = BlockedSites.IsBrowserProcess(processName)
            ? BlockedSites.Match(windowTitle, adultOnly) ?? BlockedSites.Match(browserTitle, adultOnly)
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
            if (_mode == SiteGuardMode.Off) return;

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

        if (ShouldBlock(processName, title, _mode == SiteGuardMode.AdultOnly, out string? siteName)) MinimizeWindow(hwnd, siteName);
        else _browserTitles.Queue(hwnd, SynchronizationContext.Current);
    }

    private void OnBrowserTitleRead(IntPtr hwnd, string? browserTitle)
    {
        if (_mode == SiteGuardMode.Off || hwnd != NativeMethods.GetForegroundWindow() || IsInForegroundGracePeriod(hwnd)) return;
        if (ShouldBlock(TryGetProcessName(hwnd), NativeMethods.GetWindowTitle(hwnd), browserTitle,
                _mode == SiteGuardMode.AdultOnly, out string? siteName))
            MinimizeWindow(hwnd, siteName);
    }

    private void StartForegroundGracePeriod(IntPtr hwnd)
    {
        var cancellation = new CancellationTokenSource();
        var context = SynchronizationContext.Current;
        lock (_gate)
        {
            if (_disposed || _mode == SiteGuardMode.Off)
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
                        if (_disposed || _mode == SiteGuardMode.Off
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
        if (_mode == SiteGuardMode.Off || hwnd != NativeMethods.GetForegroundWindow()) return;

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
            _mode = SiteGuardMode.Off;
            RemoveHooksLocked();
        }
    }
}

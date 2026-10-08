using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Interop;

/// <summary>
/// A dedicated background thread that owns one message-only window and runs its message loop.
/// Window messages are dispatched on that thread; other threads can run code there through <see cref="Invoke"/>.
/// <para>
/// The window is created and destroyed on its own thread, so Win32 thread-affinity rules hold.
/// <see cref="Dispose"/> posts a stop message, destroys the window on the thread, posts WM_QUIT, and joins (2 s max).
/// </para>
/// </summary>
internal sealed class MessageWindowThread : IDisposable
{
    private const int DefaultTimeoutMs = 2000;
    private static int _classCounter;

    private readonly string _className;
    private readonly Func<uint, IntPtr, IntPtr, IntPtr?>? _onMessage;
    private readonly ILogger? _logger;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ConcurrentQueue<Action> _work = new();
    // Rooted here for the lifetime of the instance; the OS keeps only the raw function pointer.
    private readonly NativeMethods.WndProcDelegate _wndProc;
    private readonly object _lifecycle = new();

    private Thread? _thread;
    private int _managedThreadId;
    private uint _nativeThreadId;
    private nint _hwnd;
    private bool _classRegistered;
    private volatile bool _disposed;

    /// <param name="name">Used in the thread name and in the (process-unique) window class name.</param>
    /// <param name="onMessage">
    /// Called on the message thread for every message not handled internally. Return null to fall through to
    /// DefWindowProc, or a value to use as the window procedure result.
    /// </param>
    public MessageWindowThread(string name, Func<uint, IntPtr, IntPtr, IntPtr?>? onMessage, ILogger? logger)
    {
        _className = $"Island.{name}.{Interlocked.Increment(ref _classCounter)}.{Guid.NewGuid():N}";
        _onMessage = onMessage;
        _logger = logger;
        _wndProc = WndProc;
    }

    /// <summary>Window handle, or zero when the window does not exist.</summary>
    public IntPtr Handle => Volatile.Read(ref _hwnd);

    /// <summary>
    /// Starts the thread and waits up to <paramref name="timeoutMs"/> for the window to exist.
    /// Idempotent. Returns true when the window is ready.
    /// </summary>
    public bool Start(int timeoutMs = DefaultTimeoutMs)
    {
        lock (_lifecycle)
        {
            if (_disposed) return false;
            if (_thread is not null) return Handle != IntPtr.Zero;

            _thread = new Thread(ThreadMain) { IsBackground = true, Name = "Island.MessageWindow." + _className };
            _thread.Start();
        }

        if (!_ready.Wait(timeoutMs))
        {
            _logger?.LogWarning("Message window thread did not start within {Timeout} ms.", timeoutMs);
            return false;
        }
        return Handle != IntPtr.Zero;
    }

    /// <summary>
    /// Runs <paramref name="func"/> on the message thread and returns its result. Blocks until it has run.
    /// Runs inline when already on the message thread. Returns false on timeout, when the thread is not running,
    /// or when <paramref name="func"/> throws.
    /// </summary>
    public bool Invoke(Func<bool> func, int timeoutMs = DefaultTimeoutMs)
    {
        IntPtr hwnd = Handle;
        if (hwnd == IntPtr.Zero) return false;

        if (Environment.CurrentManagedThreadId == _managedThreadId)
        {
            try { return func(); }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Marshalled call failed.");
                return false;
            }
        }

        // Not disposed on purpose: a timed-out item may still run later and set this after the wait gave up.
        var done = new ManualResetEventSlim(false);
        bool result = false;
        _work.Enqueue(() =>
        {
            try { result = func(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Marshalled call failed."); }
            finally { done.Set(); }
        });

        if (!NativeMethods.PostMessageW(hwnd, NativeMethods.WM_APP_INVOKE, IntPtr.Zero, IntPtr.Zero)) return false;
        if (!done.Wait(timeoutMs))
        {
            _logger?.LogWarning("Marshalled call did not complete within {Timeout} ms.", timeoutMs);
            return false;
        }
        return result;
    }

    private void ThreadMain()
    {
        _managedThreadId = Environment.CurrentManagedThreadId;
        _nativeThreadId = NativeMethods.GetCurrentThreadId();

        IntPtr instance = IntPtr.Zero;
        bool created = false;
        try
        {
            instance = NativeMethods.GetModuleHandleW(null);
            var windowClass = new NativeMethods.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = instance,
                lpszClassName = _className,
            };

            if (NativeMethods.RegisterClassExW(ref windowClass) == 0)
            {
                _logger?.LogWarning("RegisterClassExW failed (error {Error}).", Marshal.GetLastPInvokeError());
            }
            else
            {
                _classRegistered = true;
                IntPtr hwnd = NativeMethods.CreateWindowExW(0, _className, _className, 0,
                    0, 0, 0, 0, NativeMethods.HWND_MESSAGE, IntPtr.Zero, instance, IntPtr.Zero);
                if (hwnd == IntPtr.Zero)
                    _logger?.LogWarning("CreateWindowExW failed (error {Error}).", Marshal.GetLastPInvokeError());
                else
                {
                    // Publish under the lifecycle lock. If Dispose already ran (its Start timed out), nobody will stop
                    // this loop, so the window is destroyed here instead of pumping forever.
                    lock (_lifecycle)
                    {
                        if (!_disposed)
                        {
                            Volatile.Write(ref _hwnd, hwnd);
                            created = true;
                        }
                    }
                    if (!created) NativeMethods.DestroyWindow(hwnd);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not create the message window.");
        }
        finally
        {
            _ready.Set();
        }

        if (created) PumpMessages();

        // Normally Dispose already destroyed the window; this covers a loop that ended on error.
        IntPtr leftover = Handle;
        if (leftover != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(leftover);
            Volatile.Write(ref _hwnd, IntPtr.Zero);
        }
        if (_classRegistered) NativeMethods.UnregisterClassW(_className, instance);
    }

    private void PumpMessages()
    {
        while (true)
        {
            int result = NativeMethods.GetMessageW(out NativeMethods.MSG msg, IntPtr.Zero, 0, 0);
            if (result <= 0) break;   // 0: WM_QUIT, -1: error
            NativeMethods.DispatchMessageW(ref msg);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Native callback: nothing may escape from here.
        try
        {
            switch (msg)
            {
                case NativeMethods.WM_APP_INVOKE:
                    DrainWork();
                    return IntPtr.Zero;

                case NativeMethods.WM_APP_STOP:
                    NativeMethods.DestroyWindow(hwnd);
                    return IntPtr.Zero;

                case NativeMethods.WM_DESTROY:
                    if (hwnd == Handle) Volatile.Write(ref _hwnd, IntPtr.Zero);
                    NativeMethods.PostQuitMessage(0);
                    return IntPtr.Zero;
            }

            IntPtr? handled = _onMessage?.Invoke(msg, wParam, lParam);
            if (handled is { } result) return result;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Message handler failed (message {Message}).", msg);
        }
        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void DrainWork()
    {
        while (_work.TryDequeue(out Action? work))
        {
            try { work(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Marshalled work item failed."); }
        }
    }

    /// <summary>Stops the message loop, destroys the window, and joins the thread (2 s max). Idempotent.</summary>
    public void Dispose()
    {
        Thread? thread;
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
            thread = _thread;
        }
        if (thread is null) return;

        IntPtr hwnd = Handle;
        if (thread.IsAlive)
        {
            // Stop runs on the thread (DestroyWindow must be called there). WM_QUIT ends the loop; both
            // messages go through the same thread queue, so the stop is processed first.
            if (hwnd != IntPtr.Zero) NativeMethods.PostMessageW(hwnd, NativeMethods.WM_APP_STOP, IntPtr.Zero, IntPtr.Zero);
            if (_nativeThreadId != 0) NativeMethods.PostThreadMessageW(_nativeThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);

            // Joining from the message thread itself (a handler disposing its owner) would deadlock.
            if (Environment.CurrentManagedThreadId != _managedThreadId && !thread.Join(DefaultTimeoutMs))
                _logger?.LogWarning("Message window thread did not exit within {Timeout} ms.", DefaultTimeoutMs);
        }
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Island.Windows.Interop;

namespace Island.Windows.Input;

/// <summary>
/// Global Esc listener for a transient overlay. Windows often refuses activation for a window shown from a timer,
/// so the overlay can be on screen while another app owns the keyboard; a low-level hook still sees the key.
/// The hook exists only between <see cref="Start"/> and <see cref="Dispose"/>, and it swallows Esc only while armed.
/// </summary>
public sealed class EscapeKeyWatcher : IDisposable
{
    private readonly Action _escapePressed;
    private readonly EscapeKeyGate _gate = new();
    private readonly KeyboardHookNativeMethods.LowLevelKeyboardProc _callback;
    private MessageWindowThread? _thread;
    private IntPtr _hook;
    private bool _disposed;

    /// <param name="escapePressed">Runs on the hook thread when an armed Esc press starts. Marshal to the UI thread yourself.</param>
    public EscapeKeyWatcher(Action escapePressed)
    {
        _escapePressed = escapePressed ?? throw new ArgumentNullException(nameof(escapePressed));
        _callback = HookProc;
    }

    /// <summary>Installs the hook on its own message thread and arms Esc. Returns false when the hook cannot be installed.</summary>
    public bool Start()
    {
        if (_disposed) return false;
        if (_thread is not null) return Volatile.Read(ref _hook) != IntPtr.Zero;

        var thread = new MessageWindowThread("EscapeKey", onMessage: null, logger: null);
        _thread = thread;
        _gate.Arm();
        if (thread.Start() && thread.Invoke(InstallHook)) return true;

        Dispose();
        return false;
    }

    /// <summary>Disarms, removes the hook, and stops the message thread. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Disarm();

        MessageWindowThread? thread = _thread;
        _thread = null;
        if (thread is null) return;

        // Unhook on the owning thread; if that call times out, UnhookWindowsHookEx is still valid from this thread.
        if (!thread.Invoke(UnhookOnThread)) UnhookOnThread();
        thread.Dispose();
    }

    private bool InstallHook()
    {
        if (Volatile.Read(ref _hook) != IntPtr.Zero) return true;

        IntPtr module = NativeMethods.GetModuleHandleW(null);
        IntPtr hook = KeyboardHookNativeMethods.SetWindowsHookExW(KeyboardHookNativeMethods.WhKeyboardLl, _callback, module, 0);
        if (hook == IntPtr.Zero) return false;

        Interlocked.Exchange(ref _hook, hook);
        return true;
    }

    private bool UnhookOnThread()
    {
        IntPtr hook = Interlocked.Exchange(ref _hook, IntPtr.Zero);
        return hook == IntPtr.Zero || KeyboardHookNativeMethods.UnhookWindowsHookEx(hook);
    }

    private IntPtr HookProc(int code, UIntPtr wParam, IntPtr lParam)
    {
        bool swallow = false;
        try
        {
            if (code >= 0 && lParam != IntPtr.Zero && IsKeyboardMessage(wParam))
            {
                var data = Marshal.PtrToStructure<KeyboardHookNativeMethods.KbdLlHookStruct>(lParam);
                bool isKeyUp = (data.Flags & KeyboardHookNativeMethods.LlkhfUp) != 0;
                swallow = _gate.Consume(data.VkCode, isKeyUp, out bool pressed);
                if (pressed)
                {
                    try { _escapePressed(); }
                    catch (Exception ex) { Trace.TraceError("Escape notification failed: {0}", ex); }
                }
            }
        }
        catch (Exception ex)
        {
            // A native hook callback must never let a managed exception cross the boundary.
            Trace.TraceError("Escape hook callback failed: {0}", ex);
        }

        // Returning non-zero consumes the key; otherwise the event continues down the hook chain.
        return swallow ? new IntPtr(1) : KeyboardHookNativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private static bool IsKeyboardMessage(UIntPtr message) =>
        message.ToUInt32() is KeyboardHookNativeMethods.WmKeyDown
            or KeyboardHookNativeMethods.WmSysKeyDown
            or KeyboardHookNativeMethods.WmKeyUp
            or KeyboardHookNativeMethods.WmSysKeyUp;
}

/// <summary>
/// Decides which Esc events an armed watcher consumes. A press is judged once, on its first key-down edge; its auto-repeats
/// and its release follow that decision. So a press that began before arming passes through whole, and a press consumed
/// while armed keeps its release consumed even after disarming, which avoids an orphaned key-up in the foreground app.
/// </summary>
internal sealed class EscapeKeyGate
{
    internal const uint VkEscape = 0x1B;

    private readonly object _gate = new();
    private bool _armed;
    private bool _pressTracked;
    private bool _pressSwallowed;

    public void Arm()
    {
        lock (_gate) _armed = true;
    }

    public void Disarm()
    {
        lock (_gate) _armed = false;
    }

    /// <summary>Returns true when the event must be consumed. <paramref name="pressed"/> is set once per armed press.</summary>
    public bool Consume(uint virtualKey, bool isKeyUp, out bool pressed)
    {
        pressed = false;
        if (virtualKey != VkEscape) return false;

        lock (_gate)
        {
            if (isKeyUp)
            {
                bool swallowed = _pressSwallowed;
                _pressTracked = false;
                _pressSwallowed = false;
                return swallowed;
            }

            if (_pressTracked) return _pressSwallowed;   // auto-repeat of the press already decided

            _pressTracked = true;
            _pressSwallowed = _armed;
            pressed = _armed;
            return _pressSwallowed;
        }
    }
}

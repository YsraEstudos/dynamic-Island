using System.Runtime.InteropServices;
using Island.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Input;

internal interface IKeyboardHook : IDisposable
{
    bool Start(Action<KeyboardInputEvent> callback, Func<bool> readInitialState, Action<bool> applyInitialState);
}

/// <summary>Owns the low-level hook on a dedicated message-loop thread.</summary>
internal sealed class WindowsKeyboardHook : IKeyboardHook
{
    private readonly object _gate = new();
    private readonly ILogger? _logger;
    private readonly KeyboardHookNativeMethods.LowLevelKeyboardProc _hookProc;

    private MessageWindowThread? _thread;
    private Action<KeyboardInputEvent>? _callback;
    private IntPtr _hook;
    private bool _disposed;

    public WindowsKeyboardHook(ILogger? logger = null)
    {
        _logger = logger;
        _hookProc = HookProc;
    }

    public bool Start(Action<KeyboardInputEvent> callback, Func<bool> readInitialState, Action<bool> applyInitialState)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(readInitialState);
        ArgumentNullException.ThrowIfNull(applyInitialState);

        MessageWindowThread? failedThread = null;
        bool installed;
        lock (_gate)
        {
            if (_disposed) return false;
            if (_hook != IntPtr.Zero) return true;

            if (_thread is null)
            {
                var thread = new MessageWindowThread("CapsLockHook", onMessage: null, logger: _logger);
                if (!thread.Start())
                {
                    thread.Dispose();
                    return false;
                }
                _thread = thread;
            }

            _callback = callback;
            // Seed and install on the same ready message thread. No hook callback can run between these actions.
            installed = _thread.Invoke(() =>
            {
                applyInitialState(readInitialState());
                return InstallHook();
            });
            if (!installed)
            {
                _callback = null;
                failedThread = _thread;
                _thread = null;
            }
        }

        failedThread?.Dispose();
        return installed;
    }

    private bool InstallHook()
    {
        if (_disposed || _hook != IntPtr.Zero) return _hook != IntPtr.Zero;

        IntPtr module = NativeMethods.GetModuleHandleW(null);
        if (module == IntPtr.Zero)
        {
            _logger?.LogWarning("GetModuleHandleW failed while installing the Caps Lock hook (error {Error}).",
                Marshal.GetLastPInvokeError());
            return false;
        }

        IntPtr hook = KeyboardHookNativeMethods.SetWindowsHookExW(
            KeyboardHookNativeMethods.WhKeyboardLl,
            _hookProc,
            module,
            0);
        if (hook == IntPtr.Zero)
        {
            _logger?.LogWarning("SetWindowsHookExW for Caps Lock failed (error {Error}).", Marshal.GetLastPInvokeError());
            return false;
        }

        _hook = hook;
        return true;
    }

    private IntPtr HookProc(int code, UIntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0 && lParam != IntPtr.Zero && IsKeyboardMessage(wParam))
            {
                var data = Marshal.PtrToStructure<KeyboardHookNativeMethods.KbdLlHookStruct>(lParam);
                if (data.VkCode == CapsLockConstants.VkCapital)
                {
                    Action<KeyboardInputEvent>? callback = Volatile.Read(ref _callback);
                    callback?.Invoke(new KeyboardInputEvent(
                        data.VkCode,
                        (data.Flags & KeyboardHookNativeMethods.LlkhfUp) != 0,
                        (data.Flags & KeyboardHookNativeMethods.LlkhfInjected) != 0));
                }
            }
        }
        catch (Exception ex)
        {
            // A native hook must never let managed exceptions cross the callback boundary.
            TryLogDebug(ex, "Caps Lock low-level hook callback failed.");
        }

        // Always preserve the hook chain, including malformed or unhandled events.
        try { return KeyboardHookNativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam); }
        catch (Exception ex)
        {
            TryLogDebug(ex, "CallNextHookEx for Caps Lock failed.");
            return IntPtr.Zero;
        }
    }

    private void TryLogDebug(Exception exception, string message)
    {
        try { _logger?.LogDebug(exception, message); }
        catch { /* Diagnostics must not cross the native callback boundary. */ }
    }

    private static bool IsKeyboardMessage(UIntPtr message) =>
        message.ToUInt32() is KeyboardHookNativeMethods.WmKeyDown
            or KeyboardHookNativeMethods.WmSysKeyDown
            or KeyboardHookNativeMethods.WmKeyUp
            or KeyboardHookNativeMethods.WmSysKeyUp;

    public void Dispose()
    {
        MessageWindowThread? thread;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _callback = null;
            thread = _thread;
            _thread = null;
        }

        if (thread is null) return;

        bool uninstalled;
        try
        {
            uninstalled = thread.Invoke(UninstallHook);
        }
        catch (Exception ex)
        {
            uninstalled = false;
            TryLogDebug(ex, "Stopping the Caps Lock low-level hook failed.");
        }
        if (!uninstalled)
        {
            // A timed-out message-thread invocation may leave the native handle behind. UnhookWindowsHookEx
            // is safe to call from the owner process while the thread is being stopped.
            try { UninstallHook(); }
            catch (Exception ex) { TryLogDebug(ex, "Fallback cleanup of the Caps Lock hook failed."); }
        }
        thread.Dispose();
    }

    private bool UninstallHook()
    {
        IntPtr hook = _hook;
        _hook = IntPtr.Zero;
        return hook == IntPtr.Zero || KeyboardHookNativeMethods.UnhookWindowsHookEx(hook);
    }
}

internal static class WindowsCapsLockStateReader
{
    public static bool Read() => (KeyboardHookNativeMethods.GetKeyState((int)CapsLockConstants.VkCapital) & 1) != 0;
}

internal static class KeyboardHookNativeMethods
{
    public const int WhKeyboardLl = 13;
    public const uint WmKeyDown = 0x0100;
    public const uint WmKeyUp = 0x0101;
    public const uint WmSysKeyDown = 0x0104;
    public const uint WmSysKeyUp = 0x0105;
    public const uint LlkhfInjected = 0x00000010;
    public const uint LlkhfUp = 0x00000080;

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint DwExtraInfo;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr LowLevelKeyboardProc(int code, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static extern IntPtr SetWindowsHookExW(int hookType, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", EntryPoint = "CallNextHookEx", SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hook, int code, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "UnhookWindowsHookEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll", EntryPoint = "GetKeyState")]
    public static extern short GetKeyState(int virtualKey);
}

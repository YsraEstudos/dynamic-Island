using System.Runtime.InteropServices;
using Island.Windows.Interop;

namespace Island.Windows.Input;

/// <summary>Owns the hook on a dedicated message thread, independent of the WPF dispatcher.</summary>
public sealed class LowLevelMouseWatcher : IDisposable
{
    private readonly Action<int, int> _clicked;
    private readonly HookProcDelegate _callback;
    private MessageWindowThread? _thread;
    private IntPtr _hook;

    public LowLevelMouseWatcher(Action<int, int> clicked)
    {
        _clicked = clicked;
        _callback = OnMouse;
    }

    public bool Start()
    {
        if (_thread is not null) return _hook != IntPtr.Zero;
        var thread = new MessageWindowThread("OutsideClick", null, null);
        _thread = thread;
        if (thread.Start() && thread.Invoke(() =>
            (_hook = SetWindowsHookEx(14, _callback, NativeMethods.GetModuleHandleW(null), 0)) != IntPtr.Zero)) return true;
        Stop();
        return false;
    }

    public void Stop()
    {
        var thread = _thread;
        if (thread is null) return;
        _thread = null;
        thread.Invoke(() =>
        {
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            return true;
        });
        thread.Dispose();
    }

    public void Dispose() => Stop();

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (int)message is 0x0201 or 0x0204 or 0x0207 or 0x020B)
        {
            var point = Marshal.PtrToStructure<MouseData>(data);
            try { _clicked(point.X, point.Y); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError("Mouse notification failed: {0}", ex); }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    private delegate IntPtr HookProcDelegate(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData { public int X, Y; public uint Mouse, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int type, HookProcDelegate callback, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
}

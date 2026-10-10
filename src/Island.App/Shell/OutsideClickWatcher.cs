using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Island.App.Shell;

/// <summary>
/// Reports mouse presses that land outside the popup or island it watches. The island never activates, so WPF's own
/// light-dismiss for the context menu (which relies on activation/capture) misses clicks on other apps or the desktop; a
/// low-level mouse hook sees them regardless of which window owns the cursor. Only active between <see cref="Start"/> and <see cref="Stop"/>.
/// </summary>
internal sealed class OutsideClickWatcher : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    private readonly Dispatcher _dispatcher;
    private readonly Func<int, int, bool> _isInside;
    private readonly Action _onOutsideClick;
    private readonly LowLevelMouseProc _proc;   // kept in a field so the delegate outlives the hook
    private IntPtr _hook;

    /// <param name="isInside">True when a physical screen point lies on the popup or island; a press there is not outside.</param>
    /// <param name="onOutsideClick">Called on the UI thread when a button goes down outside <paramref name="isInside"/>.</param>
    public OutsideClickWatcher(Dispatcher dispatcher, Func<int, int, bool> isInside, Action onOutsideClick)
    {
        _dispatcher = dispatcher;
        _isInside = isInside;
        _onOutsideClick = onOutsideClick;
        _proc = HookProc;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose() => Stop();

    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = (int)wParam;
            if (msg is WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown)
            {
                var info = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                _dispatcher.BeginInvoke(() => CheckClick(info.X, info.Y));
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    // Measured on the UI thread, after the hook returned, so the hook never blocks input.
    private void CheckClick(int x, int y)
    {
        if (_hook == IntPtr.Zero) return;
        if (_isInside(x, y)) return;
        _onOutsideClick();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}

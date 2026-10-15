using System.Runtime.InteropServices;
using System.Text;

namespace Island.Windows.Interop;

internal static class WindowInspection
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
        { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    public static bool IsShellWindow(IntPtr hwnd)
    {
        var name = new StringBuilder(256);
        return NativeMethods.GetClassName(hwnd, name, name.Capacity) > 0 && ShellClasses.Contains(name.ToString());
    }

    public static bool CoversMonitor(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return false;
        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfoW(monitor, ref info)) return false;
        var bounds = info.rcMonitor;
        return rect.Left <= bounds.Left && rect.Top <= bounds.Top && rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom;
    }

    public static bool IsExclusiveFullscreen()
    {
        try { return NativeMethods.SHQueryUserNotificationState(out int state) >= 0 && state == NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN; }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Fullscreen notification unavailable: {0}", ex.GetType().Name); return false; }
    }

    public static unsafe string? ProcessImagePath(uint pid)
    {
        if (pid == 0) return null;
        IntPtr process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            char[] buffer = new char[32768];
            uint length = (uint)buffer.Length;
            fixed (char* pointer = buffer)
                return NativeMethods.QueryFullProcessImageNameW(process, 0, pointer, &length) ? new string(buffer, 0, (int)length) : null;
        }
        finally { NativeMethods.CloseHandle(process); }
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using Island.Windows.Interop;

namespace Island.Windows.Capture;

/// <summary>A rectangle in virtual-screen physical pixels (a monitor's rcMonitor).</summary>
internal readonly record struct ScreenRect(int X, int Y, int Width, int Height);

/// <summary>
/// Finds the monitor under the foreground window and copies it with GDI. Works for windowed and borderless games and
/// for the desktop. A game that uses exclusive full screen is not in the desktop image, so it comes out black.
/// The process is per-monitor DPI aware (app.manifest), so all rectangles are physical pixels.
/// </summary>
internal static class ScreenGrabber
{
    public static bool TryGetForegroundMonitor(out ScreenRect monitor)
    {
        // With no foreground window (lock screen, desktop switch) MonitorFromWindow returns the primary monitor.
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        IntPtr handle = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (handle == IntPtr.Zero || !NativeMethods.GetMonitorInfoW(handle, ref info))
        {
            monitor = default;
            return false;
        }

        monitor = new ScreenRect(
            info.rcMonitor.Left,
            info.rcMonitor.Top,
            info.rcMonitor.Right - info.rcMonitor.Left,
            info.rcMonitor.Bottom - info.rcMonitor.Top);
        return monitor.Width > 0 && monitor.Height > 0;
    }

    /// <summary>Copies <paramref name="source"/> into a new frame of the given size. Caller disposes the frame.</summary>
    public static DibFrame Grab(ScreenRect source, int width, int height)
    {
        var frame = new DibFrame(width, height);
        try
        {
            CaptureInto(frame, source);
            return frame;
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    /// <summary>Copies <paramref name="source"/> into an existing frame. The recorder reuses one frame for the whole recording.</summary>
    public static void CaptureInto(DibFrame frame, ScreenRect source)
    {
        IntPtr screen = CaptureNative.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError(), "GetDC failed.");

        try
        {
            frame.CopyFromScreen(screen, source);
        }
        finally
        {
            CaptureNative.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}

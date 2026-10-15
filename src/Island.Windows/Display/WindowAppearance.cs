using System.Runtime.InteropServices;
using Island.Windows.Interop;

namespace Island.Windows.Display;

public static class WindowAppearance
{
    public static void EnableDarkTitleBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int enabled = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    public static (int Left, int Top, int Right, int Bottom)? ScreenBounds(IntPtr hwnd) =>
        NativeMethods.GetWindowRect(hwnd, out var rect) ? (rect.Left, rect.Top, rect.Right, rect.Bottom) : null;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

using Island.Windows.Interop;

namespace Island.Windows.Audio;

/// <summary>Process id of the window that has focus, used to put the mixer's foreground app first. Cheap; call it freely.</summary>
public static class ForegroundProcess
{
    /// <summary>Returns 0 when there is no foreground window.</summary>
    public static int CurrentId()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        return (int)processId;
    }
}

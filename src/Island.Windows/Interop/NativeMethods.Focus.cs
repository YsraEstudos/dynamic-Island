using System.Runtime.InteropServices;

namespace Island.Windows.Interop;

/// <summary>Window title and minimize declarations used by the browser site guard.</summary>
internal static partial class NativeMethods
{
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    public const int SW_MINIMIZE = 6;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    public static partial int GetWindowTextLengthW(IntPtr hwnd);

    // A raw pointer keeps this on LibraryImport without a custom marshaller; see GetWindowTitle for the buffer.
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    public static unsafe partial int GetWindowTextW(IntPtr hwnd, char* text, int maxCount);

    /// <summary>
    /// Asynchronous on purpose: the request is queued to the window's own thread, so a hung browser cannot
    /// stall the UI thread that is handling the hook.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "ShowWindowAsync")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindowAsync(IntPtr hwnd, int cmdShow);

    /// <summary>Reads a window title. Returns an empty string when the window has none or no longer exists.</summary>
    public static unsafe string GetWindowTitle(IntPtr hwnd)
    {
        int length = GetWindowTextLengthW(hwnd);
        if (length <= 0) return string.Empty;

        // GetWindowTextW writes a terminating NUL, so the buffer needs one slot more than the text.
        char[] buffer = new char[length + 1];
        fixed (char* text = buffer)
        {
            int copied = GetWindowTextW(hwnd, text, buffer.Length);
            return new string(text, 0, copied);
        }
    }
}

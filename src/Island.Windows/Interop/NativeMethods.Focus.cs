using System.Reflection;
using System.Runtime.InteropServices;

namespace Island.Windows.Interop;

/// <summary>Window title and minimize declarations used by the browser site guard.</summary>
internal static partial class NativeMethods
{
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    public const int OBJID_CLIENT = -4;
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

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object? accessible);

    /// <summary>
    /// The browser's client root exposes the active page title, unlike a user-named window caption.
    /// Reads one property only: bookmarks, background tabs and page text are never searched.
    /// </summary>
    public static string? GetBrowserClientTitle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;

        object? accessible = null;
        try
        {
            var interfaceId = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71"); // IAccessible
            if (AccessibleObjectFromWindow(hwnd, unchecked((uint)OBJID_CLIENT), ref interfaceId, out accessible) < 0
                || accessible is null) return null;

            return accessible.GetType().InvokeMember("accName", BindingFlags.GetProperty, null, accessible,
                new object[] { CHILDID_SELF }) as string;
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or ArgumentException)
        {
            // Accessibility can disappear during navigation or when the browser closes.
            return null;
        }
        finally
        {
            if (accessible is not null && Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible);
        }
    }

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

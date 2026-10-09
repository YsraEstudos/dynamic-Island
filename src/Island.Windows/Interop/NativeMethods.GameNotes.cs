using System.Runtime.InteropServices;

namespace Island.Windows.Interop;

/// <summary>Process image lookup for the game tracker. Only PROCESS_QUERY_LIMITED_INFORMATION: no memory is read.</summary>
internal static partial class NativeMethods
{
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [LibraryImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    public static partial IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    /// <summary>
    /// Win32-style image path (flags 0). <paramref name="size"/> is the buffer length in characters on input and the
    /// number of characters written on output.
    /// </summary>
    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool QueryFullProcessImageNameW(IntPtr process, uint flags, char* buffer, uint* size);
}

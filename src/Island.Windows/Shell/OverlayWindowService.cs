using Island.Windows.Interop;
using Microsoft.Win32;

namespace Island.Windows.Shell;

public static class OverlayWindowService
{
    /// <summary>
    /// Broadcast by Explorer to top-level windows when it recreates the taskbar (after a restart or crash).
    /// Zero if the message could not be registered.
    /// </summary>
    public static readonly uint TaskbarCreatedMessage = NativeMethods.RegisterWindowMessageW("TaskbarCreated");

    /// <summary>Adds WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW (and keeps layered styles WPF set), TOPMOST. Call after the HWND exists.</summary>
    public static void ApplyOverlayStyles(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        nint ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, ex);

        // SWP_FRAMECHANGED makes the new extended style take effect; without it the style change is ignored until the next frame change.
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
    }

    /// <summary>Re-asserts HWND_TOPMOST without activating.</summary>
    public static void KeepOnTop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
}

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DynamicIsland";

    /// <summary>Enables or disables start-with-Windows through the per-user Run entry. Throws on failure so the caller can report it.</summary>
    public static void Set(bool enabled)
    {
        if (enabled)
        {
            string exe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Process path is unavailable; cannot register for startup.");
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>True when the Run entry exists. Never throws.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException
                                       or InvalidOperationException)
        {
            return false;
        }
    }
}

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>
    /// Returns null if another instance already holds the mutex. The mutex is owned by the calling thread,
    /// so Dispose must run on that same thread.
    /// </summary>
    public static SingleInstanceGuard? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, name: "Local\\" + name, createdNew: out bool createdNew);
        if (createdNew) return new SingleInstanceGuard(mutex);

        // The mutex exists and belongs to another instance. Dispose our handle; the other owner keeps its own.
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not the owning thread; the handle is still closed below.
        }
        _mutex.Dispose();
    }
}

using System.Diagnostics;
using System.Text;

namespace Island.Windows.Audio;

/// <summary>
/// What a process is called and where its executable lives. Best effort: protected or elevated processes that cannot be
/// opened give a null path, and the name then comes from the process table.
/// </summary>
internal sealed record ProcessIdentity(string? ExecutablePath, string ProcessName, string? FileDescription)
{
    // Long enough for any path Windows will report without the long-path prefix, and small enough to allocate per session.
    private const int PathCapacity = 2048;

    public static ProcessIdentity Resolve(int processId)
    {
        string? path = TryGetExecutablePath(processId);
        string processName = path is not null ? Path.GetFileNameWithoutExtension(path) : ProcessNameOf(processId);

        string? description = null;
        if (path is not null)
        {
            try { description = FileVersionInfo.GetVersionInfo(path).FileDescription; }
            catch (Exception) { /* missing or unreadable file: the name falls back to the process name */ }
        }

        return new ProcessIdentity(path, processName, description);
    }

    /// <summary>Full path of the process image, or null when the process cannot be opened.</summary>
    public static string? TryGetExecutablePath(int processId)
    {
        if (processId <= 0) return null;

        IntPtr handle = MixerNativeMethods.OpenProcess(MixerNativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (handle == IntPtr.Zero) return null;

        try
        {
            var buffer = new StringBuilder(PathCapacity);
            uint size = (uint)buffer.Capacity;
            return MixerNativeMethods.QueryFullProcessImageNameW(handle, 0, buffer, ref size)
                ? buffer.ToString(0, (int)size)
                : null;
        }
        finally
        {
            MixerNativeMethods.CloseHandle(handle);
        }
    }

    private static string ProcessNameOf(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return string.Empty;   // the process exited or cannot be inspected
        }
    }
}

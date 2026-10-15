using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Island.Core.GameNotes;
using Island.Windows.Interop;

namespace Island.Windows.GameNotes;

/// <summary>
/// Reads what the game tracker needs about a foreground window: its executable, the file description, the title,
/// and whether it covers its monitor. Reads no process memory.
/// </summary>
internal sealed class ForegroundProcessReader
{
    private const int MaxCachedDescriptions = 512;


    private readonly Dictionary<string, string?> _descriptions = new(StringComparer.OrdinalIgnoreCase);

    public ForegroundProcess Read(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        bool isOwn = pid == 0 || pid == (uint)Environment.ProcessId;

        string? exePath = isOwn ? null : TryGetImagePath(pid);
        string processName = exePath is not null ? Path.GetFileNameWithoutExtension(exePath) : TryGetProcessName(pid);
        string? description = exePath is null ? null : DescribeExecutable(exePath);

        return new ForegroundProcess(
            ProcessName: processName,
            ExePath: exePath,
            FileDescription: description,
            WindowTitle: NativeMethods.GetWindowTitle(hwnd),
            IsFullscreen: IsFullscreen(hwnd),
            IsShellWindow: IsShellWindow(hwnd),
            IsOwnProcess: isOwn);
    }

    /// <summary>Full image path of the process, or null when the process cannot be opened (access denied, exited).</summary>
    private static string? TryGetImagePath(uint pid) => WindowInspection.ProcessImagePath(pid);

    private static string TryGetProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process exited between the focus event and this read.
            return string.Empty;
        }
    }

    /// <summary>Cached per path: the version resource is read from disk, and focus changes repeat for the same apps.</summary>
    private string? DescribeExecutable(string path)
    {
        if (_descriptions.TryGetValue(path, out string? cached)) return cached;

        string? description;
        try
        {
            description = FileVersionInfo.GetVersionInfo(path).FileDescription;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            description = null;
        }

        if (_descriptions.Count >= MaxCachedDescriptions) _descriptions.Clear();
        _descriptions[path] = description;
        return description;
    }

    private static bool IsShellWindow(IntPtr hwnd) => WindowInspection.IsShellWindow(hwnd);

    /// <summary>
    /// True when the window covers its whole monitor, or the shell reports exclusive fullscreen (a D3D game). A
    /// minimized or maximized window is not fullscreen: a maximized window with an auto-hidden taskbar also covers
    /// the monitor, which is not a game.
    /// </summary>
    private static bool IsFullscreen(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd)) return false;
        if (IsExclusiveFullscreen()) return true;

        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
        if ((style & NativeMethods.WS_MAXIMIZE) != 0) return false;

        return CoversMonitor(hwnd);
    }

    private static bool CoversMonitor(IntPtr hwnd) => WindowInspection.CoversMonitor(hwnd);

    /// <summary>
    /// QUNS_RUNNING_D3D_FULL_SCREEN only. QUNS_BUSY and presentation mode are system-wide, so they would mark an
    /// unrelated window as a game. Any failure yields false.
    /// </summary>
    private static bool IsExclusiveFullscreen() => WindowInspection.IsExclusiveFullscreen();
}

using System.Runtime.InteropServices;
using Island.Windows.Interop;

namespace Island.Windows.Display;

/// <summary>All rectangles in physical pixels. DpiScale = dpi/96 (1.0, 1.25, 1.5, 2.0...).</summary>
public readonly record struct MonitorInfo(
    int Index, bool IsPrimary,
    int X, int Y, int Width, int Height,
    int WorkX, int WorkY, int WorkWidth, int WorkHeight,
    double DpiScale);

public sealed class MonitorService
{
    private const double BaseDpi = 96.0;

    /// <summary>Enumerates monitors fresh on each call. Index 0 is always the primary monitor.</summary>
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var handles = new List<IntPtr>();
        NativeMethods.MonitorEnumProc collect = (hMonitor, _, _, _) =>
        {
            handles.Add(hMonitor);
            return true;
        };
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, collect, IntPtr.Zero);

        var entries = new List<(bool IsPrimary, NativeMethods.MONITORINFO Info, double Scale)>(handles.Count);
        foreach (var handle in handles)
        {
            var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (!NativeMethods.GetMonitorInfoW(handle, ref info)) continue;
            entries.Add(((info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0, info, GetDpiScale(handle)));
        }

        if (entries.Count == 0)
            return new[] { FallbackMonitor() };

        // Stable sort keeps enumeration order for the non-primary monitors.
        var ordered = entries.OrderByDescending(e => e.IsPrimary).ToList();
        var result = new MonitorInfo[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            var (isPrimary, m, scale) = ordered[i];
            result[i] = new MonitorInfo(
                Index: i,
                IsPrimary: isPrimary,
                X: m.rcMonitor.Left,
                Y: m.rcMonitor.Top,
                Width: m.rcMonitor.Right - m.rcMonitor.Left,
                Height: m.rcMonitor.Bottom - m.rcMonitor.Top,
                WorkX: m.rcWork.Left,
                WorkY: m.rcWork.Top,
                WorkWidth: m.rcWork.Right - m.rcWork.Left,
                WorkHeight: m.rcWork.Bottom - m.rcWork.Top,
                DpiScale: scale);
        }
        return result;
    }

    /// <summary>Returns the monitor at index, falling back to primary when out of range.</summary>
    public MonitorInfo GetMonitor(int index)
    {
        var all = GetMonitors();
        return (uint)index < (uint)all.Count ? all[index] : all[0];
    }

    /// <summary>Reads effective DPI; falls back to 96 (scale 1.0) when the API is unavailable.</summary>
    private static double GetDpiScale(IntPtr hMonitor)
    {
        try
        {
            int hr = NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
            if (hr == 0 && dpiX > 0) return dpiX / BaseDpi;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // shcore is missing or too old for GetDpiForMonitor; use the default scale.
        }
        return 1.0;
    }

    /// <summary>Used when enumeration yields nothing (e.g. no interactive desktop).</summary>
    private static MonitorInfo FallbackMonitor()
    {
        int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        if (w <= 0) w = 1920;
        if (h <= 0) h = 1080;
        return new MonitorInfo(0, true, 0, 0, w, h, 0, 0, w, h, 1.0);
    }
}

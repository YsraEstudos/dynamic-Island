namespace Island.Windows.Performance;

/// <summary>Physical memory in use and total, from GlobalMemoryStatusEx.</summary>
internal static class MemoryReader
{
    public static (ulong? UsedBytes, ulong? TotalBytes) Read()
    {
        var status = new PerformanceNativeMethods.MemoryStatusEx { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PerformanceNativeMethods.MemoryStatusEx>() };
        if (!PerformanceNativeMethods.GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0) return (null, null);

        ulong used = status.TotalPhys - Math.Min(status.AvailPhys, status.TotalPhys);
        return (used, status.TotalPhys);
    }
}

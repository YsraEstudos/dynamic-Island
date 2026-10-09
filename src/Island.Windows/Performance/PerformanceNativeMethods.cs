using System.Runtime.InteropServices;

namespace Island.Windows.Performance;

/// <summary>
/// Read-only Windows and NVML queries for the performance sampler. None of them loads a driver of ours or needs
/// elevation. nvml.dll exists only where an NVIDIA driver is installed, so its calls fail with DllNotFoundException
/// there; callers treat that as "no NVIDIA GPU".
/// </summary>
internal static class PerformanceNativeMethods
{
    private const string Kernel32 = "kernel32.dll";
    private const string Nvml = "nvml.dll";

    /// <summary>NVML_TEMPERATURE_GPU.</summary>
    internal const uint NvmlTemperatureGpu = 0;

    [DllImport(Kernel32, SetLastError = true)]
    internal static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [DllImport(Kernel32, SetLastError = true)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    /// <summary>NVML returns 0 on success.</summary>
    internal const int NvmlSuccess = 0;

    [DllImport(Nvml, EntryPoint = "nvmlInit_v2")]
    internal static extern int NvmlInit();

    [DllImport(Nvml, EntryPoint = "nvmlShutdown")]
    internal static extern int NvmlShutdown();

    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetCount_v2")]
    internal static extern int NvmlDeviceGetCount(out uint count);

    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    internal static extern int NvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetTemperature")]
    internal static extern int NvmlDeviceGetTemperature(IntPtr device, uint sensorType, out uint temperature);

    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetUtilizationRates")]
    internal static extern int NvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

    [DllImport(Nvml, EntryPoint = "nvmlDeviceGetMemoryInfo")]
    internal static extern int NvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

    /// <summary>PDH_FMT_DOUBLE: values come back as doubles.</summary>
    internal const uint PdhFmtDouble = 0x00000200;
    /// <summary>PDH_MORE_DATA: the buffer is too small; the size is returned in the call.</summary>
    internal const uint PdhMoreData = 0x800007D2;
    /// <summary>ERROR_SUCCESS for PDH calls.</summary>
    internal const uint PdhSuccess = 0;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    internal static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    internal static extern uint PdhCloseQuery(IntPtr query);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NvmlUtilization
    {
        /// <summary>Percent of the last sample period the GPU was busy.</summary>
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }
}

using Island.Core.Performance;

namespace Island.Windows.Performance;

/// <summary>
/// NVIDIA GPU 0 through NVML, the library the display driver installs. Read-only and needs no elevation.
/// Only the first NVIDIA GPU is read. <see cref="IsAvailable"/> is false when there is no NVIDIA driver or no GPU.
/// </summary>
internal sealed class NvmlGpuReader : IDisposable
{
    private IntPtr _device;
    private bool _initialized;

    public NvmlGpuReader()
    {
        try
        {
            if (PerformanceNativeMethods.NvmlInit() != PerformanceNativeMethods.NvmlSuccess) return;
            _initialized = true;

            if (PerformanceNativeMethods.NvmlDeviceGetCount(out uint count) != PerformanceNativeMethods.NvmlSuccess || count == 0) return;
            if (PerformanceNativeMethods.NvmlDeviceGetHandleByIndex(0, out IntPtr device) != PerformanceNativeMethods.NvmlSuccess) return;
            _device = device;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // No NVIDIA driver on this machine: the counter reader covers the GPU instead.
        }
    }

    public bool IsAvailable => _device != IntPtr.Zero;

    /// <summary>Null when NVML is not available. Individual fields are null when a single query fails.</summary>
    public GpuReading? Read()
    {
        if (!IsAvailable) return null;

        try
        {
            double? percent = null;
            if (PerformanceNativeMethods.NvmlDeviceGetUtilizationRates(_device, out var utilization) == PerformanceNativeMethods.NvmlSuccess)
                percent = PerformanceValues.Percent(utilization.Gpu);

            double? temperature = null;
            if (PerformanceNativeMethods.NvmlDeviceGetTemperature(_device, PerformanceNativeMethods.NvmlTemperatureGpu, out uint celsius)
                == PerformanceNativeMethods.NvmlSuccess)
                temperature = PerformanceValues.Temperature(celsius);

            ulong? used = null;
            ulong? total = null;
            if (PerformanceNativeMethods.NvmlDeviceGetMemoryInfo(_device, out var memory) == PerformanceNativeMethods.NvmlSuccess)
            {
                used = memory.Used;
                total = memory.Total;
            }

            return new GpuReading(percent, temperature, used, total);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or AccessViolationException)
        {
            // The library went away or misbehaved: report unavailable instead of failing the sample.
            return null;
        }
    }

    public void Dispose()
    {
        if (!_initialized) return;
        _initialized = false;
        _device = IntPtr.Zero;
        try { PerformanceNativeMethods.NvmlShutdown(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }
}

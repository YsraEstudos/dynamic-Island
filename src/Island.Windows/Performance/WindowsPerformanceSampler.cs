using Island.Core.Abstractions;
using Island.Core.Performance;

namespace Island.Windows.Performance;

/// <summary>
/// The real sensors behind <see cref="IPerformanceSampler"/>. Every source is read on its own, so one failing source
/// only leaves its own fields null. GPU: NVIDIA through NVML when present, otherwise the GPU counters. CPU temperature:
/// CPU-named thermal zones, when the machine has them. Nothing here needs elevation or loads a driver.
/// Called from one thread at a time: <see cref="Island.Core.Performance.PerformanceMonitor"/> guarantees it.
/// </summary>
public sealed class WindowsPerformanceSampler : IPerformanceSampler
{
    private readonly CpuUsageReader _cpu = new();
    private readonly NvmlGpuReader _nvml = new();
    private readonly CounterGpuReader _counters = new();
    private readonly ThermalZoneReader _thermal = new();
    private bool _disposed;

    public PerformanceSnapshot Sample()
    {
        if (_disposed) return PerformanceSnapshot.Empty;

        double? cpuPercent = Guard<double?>(_cpu.Read, null);
        (ulong? ramUsed, ulong? ramTotal) = Guard<(ulong? UsedBytes, ulong? TotalBytes)>(MemoryReader.Read, (null, null));
        GpuReading gpu = Guard(() => _nvml.Read() ?? _counters.Read(), GpuReading.None);
        double? cpuTemp = Guard<double?>(_thermal.Read, null);

        return new PerformanceSnapshot(
            CpuPercent: cpuPercent,
            GpuPercent: gpu.Percent,
            CpuTempC: cpuTemp,
            GpuTempC: gpu.TempC,
            RamUsedBytes: ramUsed,
            RamTotalBytes: ramTotal,
            VramUsedBytes: gpu.VramUsedBytes,
            VramTotalBytes: gpu.VramTotalBytes);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _nvml.Dispose();
        _counters.Dispose();
        _thermal.Dispose();
    }

    /// <summary>One failing source must not hide the others: its value falls back instead of escaping the sample.</summary>
    private static T Guard<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}

using Island.Core.Abstractions;
using Island.Core.Performance;

namespace Island.Core.Fakes;

/// <summary>
/// Deterministic sensor values for --demo. The temperatures swing across the default limits so the alerts can be seen
/// without a hot machine.
/// </summary>
public sealed class FakePerformanceSampler : IPerformanceSampler
{
    private const ulong TotalRam = 32UL << 30;
    private const ulong TotalVram = 8UL << 30;

    private int _tick;

    public PerformanceSnapshot Sample()
    {
        int t = Interlocked.Increment(ref _tick);
        double cpu = 45 + 40 * Math.Sin(t / 6.0);
        double gpu = 40 + 45 * Math.Sin(t / 9.0 + 1.0);
        double cpuTemp = 62 + 30 * Math.Sin(t / 11.0);
        double gpuTemp = 60 + 28 * Math.Sin(t / 13.0 + 2.0);
        double ram = 0.55 + 0.1 * Math.Sin(t / 17.0);
        double vram = 0.4 + 0.3 * Math.Sin(t / 9.0);

        return new PerformanceSnapshot(
            CpuPercent: PerformanceValues.Percent(cpu),
            GpuPercent: PerformanceValues.Percent(gpu),
            CpuTempC: PerformanceValues.Temperature(cpuTemp),
            GpuTempC: PerformanceValues.Temperature(gpuTemp),
            RamUsedBytes: (ulong)(TotalRam * ram),
            RamTotalBytes: TotalRam,
            VramUsedBytes: (ulong)(TotalVram * vram),
            VramTotalBytes: TotalVram);
    }

    public void Dispose()
    {
    }
}

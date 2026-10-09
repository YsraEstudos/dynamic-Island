using Island.Core.Performance;

namespace Island.Windows.Performance;

/// <summary>
/// Whole-machine CPU use from GetSystemTimes deltas. The first read has no previous sample and returns null.
/// Kernel time includes idle time, so busy time is (kernel + user - idle). Called from one thread at a time.
/// </summary>
internal sealed class CpuUsageReader
{
    private long _idle;
    private long _kernel;
    private long _user;
    private bool _hasPrevious;

    public double? Read()
    {
        if (!PerformanceNativeMethods.GetSystemTimes(out long idle, out long kernel, out long user)) return null;

        double? percent = null;
        if (_hasPrevious)
        {
            long idleDelta = idle - _idle;
            long totalDelta = (kernel - _kernel) + (user - _user);
            if (totalDelta > 0) percent = PerformanceValues.Percent(100.0 * (totalDelta - idleDelta) / totalDelta);
        }

        _idle = idle;
        _kernel = kernel;
        _user = user;
        _hasPrevious = true;
        return percent;
    }
}

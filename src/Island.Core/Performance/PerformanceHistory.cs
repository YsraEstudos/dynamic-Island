namespace Island.Core.Performance;

/// <summary>Recent CPU and GPU readings for the sparklines. Thread-safe. Unknown readings stay as null gaps.</summary>
public sealed class PerformanceHistory
{
    /// <summary>One minute of readings at the 1 s rate the widget samples at.</summary>
    public const int Capacity = 60;

    private readonly object _gate = new();
    private readonly RingBuffer<double?> _cpu = new(Capacity);
    private readonly RingBuffer<double?> _gpu = new(Capacity);

    public void Add(PerformanceSnapshot snapshot)
    {
        lock (_gate)
        {
            _cpu.Add(snapshot.CpuPercent);
            _gpu.Add(snapshot.GpuPercent);
        }
    }

    /// <summary>CPU percentages, oldest first.</summary>
    public double?[] Cpu()
    {
        lock (_gate) return _cpu.ToArray();
    }

    /// <summary>GPU percentages, oldest first.</summary>
    public double?[] Gpu()
    {
        lock (_gate) return _gpu.ToArray();
    }
}

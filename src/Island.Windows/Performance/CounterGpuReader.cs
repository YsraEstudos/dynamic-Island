using System.ComponentModel;
using Island.Core.Performance;

namespace Island.Windows.Performance;

/// <summary>
/// GPU load and video memory from the "GPU Engine" and "GPU Adapter Memory" counters, which work on NVIDIA, AMD and
/// Intel under WDDM 2.0 or later. Temperature is not available this way. The queries are rebuilt every
/// <see cref="RefreshInterval"/> so that new processes and engines are picked up; between rebuilds each sample is one
/// collect call. Called from one thread at a time.
/// </summary>
internal sealed class CounterGpuReader : IDisposable
{
    private const string EngineCounterPath = @"\GPU Engine(*)\Utilization Percentage";
    private const string MemoryCounterPath = @"\GPU Adapter Memory(*)\Dedicated Usage";

    /// <summary>How often the queries are rebuilt (processes and engines start and stop).</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);
    /// <summary>After the counters cannot be opened (no WDDM driver, counters disabled), the next attempt waits this long.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(60);

    private PdhWildcardCounter? _engines;
    private PdhWildcardCounter? _memory;
    private DateTime _nextRebuildUtc = DateTime.MinValue;
    private bool _disposed;

    public GpuReading Read()
    {
        if (_disposed) return GpuReading.None;

        try
        {
            if (DateTime.UtcNow >= _nextRebuildUtc) Rebuild();
            if (_engines is null || _memory is null) return GpuReading.None;

            double? percent = GpuEngineUsage.Busiest(_engines.Collect());
            ulong? used = GpuEngineUsage.DedicatedBytes(_memory.Collect());
            return new GpuReading(percent, null, used, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or PlatformNotSupportedException)
        {
            CloseQueries();
            _nextRebuildUtc = DateTime.UtcNow + RetryInterval;
            return GpuReading.None;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        CloseQueries();
    }

    private void Rebuild()
    {
        CloseQueries();
        _nextRebuildUtc = DateTime.UtcNow + RefreshInterval;
        _engines = new PdhWildcardCounter(EngineCounterPath);
        _memory = new PdhWildcardCounter(MemoryCounterPath);
    }

    private void CloseQueries()
    {
        _engines?.Dispose();
        _memory?.Dispose();
        _engines = null;
        _memory = null;
    }
}

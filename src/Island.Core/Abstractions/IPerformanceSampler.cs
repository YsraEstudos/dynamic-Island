using Island.Core.Performance;

namespace Island.Core.Abstractions;

/// <summary>
/// Reads one performance sample of the machine. Called from a background thread, one call at a time.
/// Never throws: a sensor that is missing or needs elevation is reported as null in the snapshot.
/// </summary>
public interface IPerformanceSampler : IDisposable
{
    PerformanceSnapshot Sample();
}

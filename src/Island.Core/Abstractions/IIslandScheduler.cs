namespace Island.Core.Abstractions;

/// <summary>Abstraction over timers so the coordinator is deterministic in tests.</summary>
public interface IIslandScheduler
{
    /// <summary>Runs <paramref name="callback"/> once after <paramref name="delay"/>. Disposing cancels it.</summary>
    IDisposable Schedule(TimeSpan delay, Action callback);
}

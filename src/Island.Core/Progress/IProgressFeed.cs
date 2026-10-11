namespace Island.Core.Progress;

/// <summary>
/// Source of progress updates (the named-pipe server in the real app, a fake in demo mode). Events may fire on any
/// thread. Implementations must never throw from <see cref="Start"/> and must not poll.
/// </summary>
public interface IProgressFeed : IDisposable
{
    /// <summary>A valid update arrived.</summary>
    event Action<ProgressUpdate>? Updated;

    /// <summary>The sender went away; these task ids will not report again.</summary>
    event Action<IReadOnlyList<string>>? Dropped;

    /// <summary>Starts listening. Repeated calls do nothing.</summary>
    void Start();
}

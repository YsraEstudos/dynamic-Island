using Island.Core.Progress;

namespace Island.Core.Fakes;

/// <summary>
/// In-memory <see cref="IProgressFeed"/> for --demo and tests. <see cref="Push"/> and <see cref="Drop"/> raise the events
/// synchronously on the calling thread. Start does nothing: updates can be pushed at any time.
/// </summary>
public sealed class FakeProgressFeed : IProgressFeed
{
    public event Action<ProgressUpdate>? Updated;

    public event Action<IReadOnlyList<string>>? Dropped;

    public void Start() { }

    public void Push(ProgressUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        Updated?.Invoke(update);
    }

    public void Drop(params string[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        Dropped?.Invoke(ids);
    }

    public void Dispose()
    {
        Updated = null;
        Dropped = null;
    }
}

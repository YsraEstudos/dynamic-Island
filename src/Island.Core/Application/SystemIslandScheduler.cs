using Island.Core.Abstractions;

namespace Island.Core.Application;

/// <summary>
/// Production scheduler backed by <see cref="System.Threading.Timer"/>. Each call creates an independent one-shot.
/// Disposing before the callback starts guarantees it never runs. Once the callback has started, disposing
/// does not wait for it, so the coordinator's generation check is what makes late callbacks harmless.
/// Callbacks run on a thread-pool thread, never synchronously inside <see cref="Schedule"/>.
/// </summary>
public sealed class SystemIslandScheduler : IIslandScheduler
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        if (delay > MaxDelay) delay = MaxDelay;
        return new OneShot(delay, callback);
    }

    private sealed class OneShot : IDisposable
    {
        private readonly object _gate = new();
        private Action? _callback;
        private Timer? _timer;

        public OneShot(TimeSpan delay, Action callback)
        {
            _callback = callback;
            lock (_gate)
            {
                // Created under the lock so Fire, which takes the lock first, always sees a non-null _timer.
                _timer = new Timer(_ => Fire(), null, delay, Timeout.InfiniteTimeSpan);
            }
        }

        private void Fire()
        {
            Action? callback;
            Timer? timer;
            lock (_gate)
            {
                callback = _callback;
                _callback = null;
                timer = _timer;
                _timer = null;
            }

            timer?.Dispose();
            callback?.Invoke();
        }

        public void Dispose()
        {
            Timer? timer;
            lock (_gate)
            {
                _callback = null;
                timer = _timer;
                _timer = null;
            }

            timer?.Dispose();
        }
    }
}

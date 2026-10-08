using Island.Core.Abstractions;

namespace Island.Core.Fakes;

/// <summary>
/// Deterministic scheduler for tests. Time moves only through <see cref="Advance"/>.
/// Callbacks run outside the internal lock, so they may schedule or cancel timers re-entrantly.
/// </summary>
public sealed class ManualScheduler : IIslandScheduler
{
    private sealed class Entry
    {
        public required TimeSpan Due { get; init; }
        public required long Sequence { get; init; }
        public required Action Callback { get; init; }
    }

    private readonly object _gate = new();
    private readonly List<Entry> _entries = new();
    private TimeSpan _now = TimeSpan.Zero;
    private long _sequence;

    /// <summary>Virtual time elapsed since construction.</summary>
    public TimeSpan Now
    {
        get
        {
            lock (_gate) return _now;
        }
    }

    /// <summary>Live (scheduled, not yet fired, not cancelled) callbacks.</summary>
    public int PendingCount
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    /// <summary>Total callbacks that have actually run.</summary>
    public int FiredCount { get; private set; }

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_gate)
        {
            var entry = new Entry
            {
                Due = _now + (delay < TimeSpan.Zero ? TimeSpan.Zero : delay),
                Sequence = _sequence++,
                Callback = callback,
            };
            _entries.Add(entry);
            return new Handle(this, entry);
        }
    }

    /// <summary>Advances virtual time by <paramref name="by"/>, firing every callback that comes due, in due-time order.</summary>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(by));

        TimeSpan target;
        lock (_gate) target = _now + by;

        while (true)
        {
            Entry? next;
            lock (_gate)
            {
                next = _entries
                    .Where(e => e.Due <= target)
                    .OrderBy(e => e.Due)
                    .ThenBy(e => e.Sequence)
                    .FirstOrDefault();

                if (next is null)
                {
                    _now = target;
                    return;
                }

                // Virtual time jumps to the callback's due time, so callbacks rescheduled from it are relative to that moment.
                _now = next.Due;
                _entries.Remove(next);
                FiredCount++;
            }

            next.Callback();
        }
    }

    private void Remove(Entry entry)
    {
        lock (_gate) _entries.Remove(entry);
    }

    private sealed class Handle : IDisposable
    {
        private readonly ManualScheduler _owner;
        private readonly Entry _entry;

        public Handle(ManualScheduler owner, Entry entry)
        {
            _owner = owner;
            _entry = entry;
        }

        public void Dispose() => _owner.Remove(_entry);
    }
}

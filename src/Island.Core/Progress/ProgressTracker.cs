using Island.Core.Abstractions;
using Island.Core.Models;

namespace Island.Core.Progress;

/// <summary>
/// Keeps the long tasks reported by an <see cref="IProgressFeed"/>: what the island lists, the whole-island bar, and
/// the completion notices. Thread-safe: updates arrive on the feed's thread, expiry on the scheduler's thread.
/// <see cref="Changed"/> fires only when something observable changed, and it carries no data: read the snapshot.
/// </summary>
public sealed class ProgressTracker : IDisposable
{
    /// <summary>Most tasks kept at once. A new task beyond this drops the oldest one.</summary>
    public const int MaxTasks = 16;

    /// <summary>How long a finished task stays in the list.</summary>
    public static readonly TimeSpan FinishedLinger = TimeSpan.FromSeconds(4);

    /// <summary>Ids of finished tasks remembered after they leave the list, so a late update cannot bring them back.</summary>
    private const int RememberedFinishedIds = 64;

    private readonly IProgressFeed _feed;
    private readonly IIslandScheduler _scheduler;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly List<Entry> _entries = new();   // creation order: index 0 is the oldest
    private readonly Queue<string> _finishedOrder = new();
    private readonly HashSet<string> _finishedIds = new(StringComparer.Ordinal);
    private bool _started;
    private bool _disposed;

    public ProgressTracker(IProgressFeed feed, IIslandScheduler scheduler, Func<DateTimeOffset> clock)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(clock);

        _feed = feed;
        _scheduler = scheduler;
        _clock = clock;
        _feed.Updated += OnUpdated;
        _feed.Dropped += OnDropped;
    }

    /// <summary>The visible tasks or the summary changed. May fire on any thread.</summary>
    public event Action? Changed;

    /// <summary>A task finished. Raised once per task, on the transition to Done or Error.</summary>
    public event Action<Notice>? NoticeRaised;

    /// <summary>Whole-island view of the active tasks.</summary>
    public ProgressSummary Summary
    {
        get
        {
            lock (_gate) return Summarize();
        }
    }

    /// <summary>
    /// Visible tasks: active ones first, oldest first, then finished ones, most recent first. The ETA is computed
    /// against the clock at the moment of this call.
    /// </summary>
    public IReadOnlyList<ProgressTaskInfo> Snapshot()
    {
        lock (_gate)
        {
            DateTimeOffset now = _clock();
            var result = new List<ProgressTaskInfo>(_entries.Count);
            foreach (Entry entry in _entries.Where(e => e.Info.IsActive).OrderBy(e => e.Info.StartedAt))
            {
                result.Add(WithEta(entry.Info, now));
            }
            foreach (Entry entry in _entries.Where(e => !e.Info.IsActive).OrderByDescending(e => e.Info.StartedAt))
            {
                result.Add(WithEta(entry.Info, now));
            }
            return result;
        }
    }

    /// <summary>Starts the feed. Repeated calls and calls after <see cref="Dispose"/> do nothing.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }
        _feed.Start();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Entry entry in _entries) entry.Expiry?.Dispose();
            _entries.Clear();
        }
        _feed.Updated -= OnUpdated;
        _feed.Dropped -= OnDropped;
    }

    private void OnUpdated(ProgressUpdate update)
    {
        bool changed;
        Notice? notice = null;
        lock (_gate)
        {
            if (_disposed || _finishedIds.Contains(update.Id)) return;

            Entry? entry = _entries.Find(e => e.Info.Id == update.Id);
            if (entry is null)
            {
                // A cancellation for a task we never showed has nothing to display.
                if (update.State == ProgressState.Cancelled)
                {
                    Remember(update.Id);
                    return;
                }

                if (_entries.Count >= MaxTasks) RemoveEntry(_entries[0]);
                entry = new Entry(new ProgressTaskInfo(
                    update.Id, update.Title, update.Category, update.State, update.Percent, update.Message, _clock(), null));
                _entries.Add(entry);
                changed = true;
            }
            else
            {
                ProgressTaskInfo next = entry.Info with
                {
                    Title = update.Title,
                    Category = update.Category,
                    State = update.State,
                    Percent = update.Percent,
                    Message = update.Message,
                };
                if (next == entry.Info) return;

                entry.Info = next;
                changed = true;
            }

            switch (update.State)
            {
                case ProgressState.Cancelled:
                    RemoveEntry(entry);
                    Remember(update.Id);
                    break;

                case ProgressState.Done:
                    notice = new Notice("Concluido", update.Title, "check");
                    Finish(entry);
                    break;

                case ProgressState.Error:
                    notice = new Notice("Falhou", string.IsNullOrEmpty(update.Message) ? update.Title : $"{update.Title}: {update.Message}", "task-error");
                    Finish(entry);
                    break;
            }
        }

        if (changed) Changed?.Invoke();
        if (notice is not null) NoticeRaised?.Invoke(notice);
    }

    private void OnDropped(IReadOnlyList<string> ids)
    {
        bool changed = false;
        lock (_gate)
        {
            if (_disposed) return;
            foreach (string id in ids)
            {
                Entry? entry = _entries.Find(e => e.Info.Id == id);
                if (entry is null || !entry.Info.IsActive) continue;

                RemoveEntry(entry);
                changed = true;
            }
        }

        if (changed) Changed?.Invoke();
    }

    /// <summary>Marks the task finished and schedules its removal. Called under the lock.</summary>
    private void Finish(Entry entry)
    {
        Remember(entry.Info.Id);
        entry.Expiry = _scheduler.Schedule(FinishedLinger, () => Expire(entry));
    }

    private void Expire(Entry entry)
    {
        bool removed;
        lock (_gate)
        {
            removed = !_disposed && _entries.Remove(entry);
        }

        if (removed) Changed?.Invoke();
    }

    /// <summary>Removes the task and cancels its pending expiry. Called under the lock.</summary>
    private void RemoveEntry(Entry entry)
    {
        entry.Expiry?.Dispose();
        entry.Expiry = null;
        _entries.Remove(entry);
    }

    private void Remember(string id)
    {
        if (!_finishedIds.Add(id)) return;

        _finishedOrder.Enqueue(id);
        if (_finishedOrder.Count > RememberedFinishedIds) _finishedIds.Remove(_finishedOrder.Dequeue());
    }

    private ProgressSummary Summarize()
    {
        int active = 0;
        int known = 0;
        double sum = 0;
        foreach (Entry entry in _entries)
        {
            if (!entry.Info.IsActive) continue;

            active++;
            if (entry.Info.Percent is { } percent)
            {
                sum += percent;
                known++;
            }
        }

        if (active == 0) return ProgressSummary.None;
        double fraction = known == 0 ? 0.0 : sum / known / 100.0;
        return new ProgressSummary(active, fraction, known == 0);
    }

    /// <summary>ETA = elapsed * (100 - p) / p, for a running task with 3 to 99 percent. Never negative, whole seconds.</summary>
    private static ProgressTaskInfo WithEta(ProgressTaskInfo info, DateTimeOffset now)
    {
        if (info.State != ProgressState.Running || info.Percent is not { } percent || percent < 3 || percent >= 100)
        {
            return info;
        }

        double elapsed = Math.Max(0, (now - info.StartedAt).TotalSeconds);
        double remaining = elapsed * (100 - percent) / percent;
        return info with { Eta = TimeSpan.FromSeconds(Math.Round(remaining)) };
    }

    private sealed class Entry
    {
        public Entry(ProgressTaskInfo info)
        {
            Info = info;
        }

        /// <summary>The stored task. Its Eta stays null; <see cref="Snapshot"/> fills it in.</summary>
        public ProgressTaskInfo Info { get; set; }

        /// <summary>Pending removal of a finished task.</summary>
        public IDisposable? Expiry { get; set; }
    }
}

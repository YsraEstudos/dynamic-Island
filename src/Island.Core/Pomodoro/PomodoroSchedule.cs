using System.Text.Json;
using Island.Core.Abstractions;

namespace Island.Core.Pomodoro;

/// <summary>What a scheduled start runs: a normal Focus plan, or an Angry (locked) plan.</summary>
public enum ScheduledStartMode { Focus, Angry }

/// <summary>One pending scheduled start. <see cref="Id"/> identifies it, so it can be removed on its own.</summary>
public sealed record ScheduledStart(Guid Id, DateTimeOffset At, ScheduledStartMode Mode, int Cycles);

/// <summary>
/// Holds scheduled starts of a pomodoro plan, each at its own date and time. When a time comes it starts the plan
/// by itself: a fresh Focus plan, or an Angry session. It never overrides a plan that is already locked by an
/// Angry session. Only the earliest start is armed on the scheduler at a time. With a store path the starts are
/// saved after every change and loaded again on construction, so a start for tomorrow survives a restart.
/// Thread-safe.
/// </summary>
public sealed class PomodoroSchedule : IDisposable
{
    // The start is checked against the clock at least this often, so a PC that slept past the time starts it on wake
    // instead of waiting out a long one-shot delay that does not count the sleep.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    // A saved start this far in the past when the app loads (the PC was off) is dropped instead of firing late.
    private static readonly TimeSpan LoadGrace = TimeSpan.FromMinutes(15);

    private static readonly Comparison<ScheduledStart> ByTime = (a, b) => a.At.CompareTo(b.At);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly PomodoroTimer _timer;
    private readonly AngryPomodoro _angry;
    private readonly IIslandScheduler _scheduler;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string? _storePath;

    // Guards the fields below. Timer calls and events happen outside it.
    private readonly object _gate = new();
    // Sorted by At, earliest first.
    private readonly List<ScheduledStart> _starts = new();
    private IDisposable? _handle;
    // Bumped whenever the armed callback is replaced or cancelled, so a callback that was already dispatched does nothing.
    private int _generation;
    private bool _disposed;

    /// <summary>
    /// <paramref name="storePath"/> is the JSON file the starts are saved to; null keeps them in memory only.
    /// A missing or broken file means no saved starts.
    /// </summary>
    public PomodoroSchedule(PomodoroTimer timer, AngryPomodoro angry, IIslandScheduler scheduler,
        Func<DateTimeOffset>? clock = null, string? storePath = null)
    {
        _timer = timer ?? throw new ArgumentNullException(nameof(timer));
        _angry = angry ?? throw new ArgumentNullException(nameof(angry));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _clock = clock ?? (() => DateTimeOffset.Now);
        _storePath = storePath;

        // Starts already due are armed with zero delay, so they fire on the scheduler, after the caller can subscribe.
        var now = _clock();
        lock (_gate)
        {
            if (_storePath is not null) _starts.AddRange(ReadStore(_storePath, now));
            RearmLocked(now);
        }
    }

    /// <summary>The scheduled starts, earliest first. A snapshot: later changes do not show in it.</summary>
    public IReadOnlyList<ScheduledStart> Starts
    {
        get
        {
            lock (_gate) return _starts.ToArray();
        }
    }

    /// <summary>The earliest scheduled start, or null when nothing is scheduled.</summary>
    public ScheduledStart? Pending
    {
        get
        {
            lock (_gate) return _starts.Count > 0 ? _starts[0] : null;
        }
    }

    /// <summary>Raised after a start is added, removed, cancelled or fired. Arbitrary thread, never under the internal lock.</summary>
    public event Action? Changed;

    /// <summary>
    /// Schedules a start at <paramref name="at"/>. <paramref name="cycles"/> is clamped to
    /// <see cref="PomodoroTimer.MinCycles"/>..<see cref="PomodoroTimer.MaxCycles"/>. A time that is not ahead of now
    /// fires at once and is not stored, so the result is null then. Otherwise returns the stored start, or null when
    /// disposed.
    /// </summary>
    public ScheduledStart? Add(DateTimeOffset at, ScheduledStartMode mode, int cycles)
    {
        var start = new ScheduledStart(Guid.NewGuid(), at, mode, Math.Clamp(cycles, PomodoroTimer.MinCycles, PomodoroTimer.MaxCycles));

        bool due;
        lock (_gate)
        {
            if (_disposed) return null;

            var now = _clock();
            due = at <= now;
            if (!due)
            {
                _starts.Add(start);
                _starts.Sort(ByTime);
                Save();
                RearmLocked(now);
            }
        }

        if (due)
        {
            RunDue(start);
            return null;
        }

        RaiseChanged();
        return start;
    }

    /// <summary>Removes one start by its <see cref="ScheduledStart.Id"/>. Returns false when no such start exists.</summary>
    public bool Remove(Guid id)
    {
        lock (_gate)
        {
            if (_disposed) return false;

            int index = _starts.FindIndex(s => s.Id == id);
            if (index < 0) return false;

            _starts.RemoveAt(index);
            Save();
            RearmLocked(_clock());
        }

        RaiseChanged();
        return true;
    }

    /// <summary>Removes all scheduled starts.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (_disposed || _starts.Count == 0) return;

            _starts.Clear();
            Save();
            RearmLocked(_clock());
        }

        RaiseChanged();
    }

    /// <summary>
    /// Next local occurrence of a clock time: today if it is still ahead of <paramref name="now"/>, otherwise tomorrow.
    /// The result keeps the offset of <paramref name="now"/>.
    /// </summary>
    public static DateTimeOffset NextOccurrence(TimeOnly time, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        var candidate = new DateTimeOffset(today.ToDateTime(time), now.Offset);
        return candidate > now ? candidate : candidate.AddDays(1);
    }

    /// <summary>A local date and time as an offset time, keeping the offset of <paramref name="now"/>.</summary>
    public static DateTimeOffset At(DateOnly date, TimeOnly time, DateTimeOffset now) =>
        new(date.ToDateTime(time), now.Offset);

    /// <summary>Drops every start in memory without firing it. Saved starts stay on disk for the next run.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _starts.Clear();
            CancelLocked();
        }
    }

    // Caller holds the lock. Cancels the armed callback and arms one for the earliest start, if there is any.
    private void RearmLocked(DateTimeOffset now)
    {
        CancelLocked();
        if (_starts.Count == 0) return;

        ArmLocked(_generation, _starts[0].At - now);
    }

    // Caller holds the lock.
    private void ArmLocked(int generation, TimeSpan left)
    {
        TimeSpan delay = left <= TimeSpan.Zero ? TimeSpan.Zero : left < CheckInterval ? left : CheckInterval;
        _handle = _scheduler.Schedule(delay, () => Check(generation));
    }

    // Takes the earliest start once the clock reaches it; until then waits for the next check.
    private void Check(int generation)
    {
        ScheduledStart due;
        lock (_gate)
        {
            if (_disposed || generation != _generation || _starts.Count == 0) return;

            var now = _clock();
            if (_starts[0].At > now)
            {
                RearmLocked(now);
                return;
            }

            due = _starts[0];
            _starts.RemoveAt(0);
            Save();
            // Arms the next start. When it is due too it fires at once, so several due starts run in time order.
            RearmLocked(now);
        }

        RunDue(due);
    }

    // Runs outside the lock: announces that the start was taken, then starts the plan.
    private void RunDue(ScheduledStart start)
    {
        RaiseChanged();

        // An Angry session owns the timer; a new plan must not replace it.
        if (_timer.ControlsLocked || _angry.IsSessionActive) return;

        _timer.Reset();
        if (start.Mode == ScheduledStartMode.Focus)
        {
            _timer.StartPlan(start.Cycles);
        }
        else
        {
            // Reset keeps the phase when no plan was active, so force a fresh focus before engaging.
            if (_timer.Phase != PomodoroPhase.Focus) _timer.SetPhase(PomodoroPhase.Focus);
            _angry.Engage(start.Cycles);
        }
    }

    // Caller holds the lock. Cancels the live callback and bumps the generation so a dispatched one does nothing.
    private void CancelLocked()
    {
        _generation++;
        var handle = _handle;
        _handle = null;
        handle?.Dispose();
    }

    // Caller holds the lock. Writes to a temporary file and moves it over the store, so a crash never leaves half a file.
    private void Save()
    {
        if (_storePath is null) return;

        var file = new StoreFile
        {
            Starts = _starts.Select(s => new StoreEntry { Id = s.Id, At = s.At, Mode = s.Mode.ToString(), Cycles = s.Cycles }).ToList(),
        };
        string temporary = _storePath + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(_storePath);
            if (directory is not null) Directory.CreateDirectory(directory);

            File.WriteAllText(temporary, JsonSerializer.Serialize(file, JsonOptions));
            File.Move(temporary, _storePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Dropped on purpose: the next change saves the whole list again.
        }
    }

    // Reads the saved starts, skipping entries that cannot be understood or that are too far in the past.
    private static List<ScheduledStart> ReadStore(string path, DateTimeOffset now)
    {
        var result = new List<ScheduledStart>();
        try
        {
            if (!File.Exists(path)) return result;

            StoreFile? file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(path), JsonOptions);
            if (file?.Starts is null) return result;

            foreach (var entry in file.Starts)
            {
                if (entry is null || !Enum.TryParse(entry.Mode, out ScheduledStartMode mode) || !Enum.IsDefined(mode)) continue;
                if (entry.At < now - LoadGrace) continue;

                result.Add(new ScheduledStart(entry.Id, entry.At, mode,
                    Math.Clamp(entry.Cycles, PomodoroTimer.MinCycles, PomodoroTimer.MaxCycles)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            result.Clear();
        }

        result.Sort(ByTime);
        return result;
    }

    private void RaiseChanged() => Changed?.Invoke();

    private sealed class StoreFile
    {
        public List<StoreEntry>? Starts { get; set; }
    }

    private sealed class StoreEntry
    {
        public Guid Id { get; set; }
        public DateTimeOffset At { get; set; }
        public string? Mode { get; set; }
        public int Cycles { get; set; }
    }
}

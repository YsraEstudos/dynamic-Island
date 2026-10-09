using Island.Core.Abstractions;

namespace Island.Core.Pomodoro;

/// <summary>What a scheduled start runs: a normal Focus plan, or an Angry (locked) plan.</summary>
public enum ScheduledStartMode { Focus, Angry }

/// <summary>One pending scheduled start.</summary>
public sealed record ScheduledStart(DateTimeOffset At, ScheduledStartMode Mode, int Cycles);

/// <summary>
/// Holds at most one pending start of a pomodoro plan at a clock time. When the time comes it starts the plan
/// by itself: a fresh Focus plan, or an Angry session. It never overrides a plan that is already locked by an
/// Angry session. Thread-safe.
/// </summary>
public sealed class PomodoroSchedule : IDisposable
{
    // The start is checked against the clock at least this often, so a PC that slept past the time starts it on wake
    // instead of waiting out a long one-shot delay that does not count the sleep.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private readonly PomodoroTimer _timer;
    private readonly AngryPomodoro _angry;
    private readonly IIslandScheduler _scheduler;
    private readonly Func<DateTimeOffset> _clock;

    // Guards the fields below. Timer calls and events happen outside it.
    private readonly object _gate = new();
    private ScheduledStart? _pending;
    private IDisposable? _handle;
    // Bumped whenever the pending start is set, cancelled or fired, so a callback that was already dispatched does nothing.
    private int _generation;
    private bool _disposed;

    public PomodoroSchedule(PomodoroTimer timer, AngryPomodoro angry, IIslandScheduler scheduler, Func<DateTimeOffset>? clock = null)
    {
        _timer = timer ?? throw new ArgumentNullException(nameof(timer));
        _angry = angry ?? throw new ArgumentNullException(nameof(angry));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>The pending start, or null when nothing is scheduled.</summary>
    public ScheduledStart? Pending
    {
        get
        {
            lock (_gate) return _pending;
        }
    }

    /// <summary>Raised after <see cref="Pending"/> changes: set, cancelled or fired. Arbitrary thread, never under the internal lock.</summary>
    public event Action? Changed;

    /// <summary>
    /// Schedules a start at <paramref name="at"/>, replacing any pending one. <paramref name="cycles"/> is clamped to
    /// <see cref="PomodoroTimer.MinCycles"/>..<see cref="PomodoroTimer.MaxCycles"/>. A time that is not ahead of now fires at once.
    /// </summary>
    public void Set(DateTimeOffset at, ScheduledStartMode mode, int cycles)
    {
        var start = new ScheduledStart(at, mode, Math.Clamp(cycles, PomodoroTimer.MinCycles, PomodoroTimer.MaxCycles));
        var now = _clock();

        int generation;
        lock (_gate)
        {
            if (_disposed) return;
            CancelLocked();
            _pending = start;
            generation = _generation;
            if (at > now) ArmLocked(generation, at - now);
        }

        if (at <= now)
        {
            Fire(generation);
            return;
        }

        RaiseChanged();
    }

    /// <summary>Drops the pending start, if any.</summary>
    public void Cancel()
    {
        bool hadPending;
        lock (_gate)
        {
            if (_disposed) return;
            hadPending = _pending is not null;
            CancelLocked();
            _pending = null;
        }

        if (hadPending) RaiseChanged();
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

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending = null;
            CancelLocked();
        }
    }

    // Caller holds the lock.
    private void ArmLocked(int generation, TimeSpan left) =>
        _handle = _scheduler.Schedule(left < CheckInterval ? left : CheckInterval, () => Check(generation));

    // Fires once the clock reaches the pending time; until then waits for the next check.
    private void Check(int generation)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation || _pending is null) return;
            var left = _pending.At - _clock();
            if (left > TimeSpan.Zero)
            {
                ArmLocked(generation, left);
                return;
            }
        }

        Fire(generation);
    }

    private void Fire(int generation)
    {
        ScheduledStart start;
        lock (_gate)
        {
            if (_disposed || generation != _generation || _pending is null) return;
            start = _pending;
            _pending = null;
            // The fired timer is spent.
            _handle = null;
            _generation++;
        }

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

    private void RaiseChanged() => Changed?.Invoke();
}

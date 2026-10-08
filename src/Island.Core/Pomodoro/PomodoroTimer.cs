using Island.Core.Abstractions;
using Island.Core.Configuration;

namespace Island.Core.Pomodoro;

public enum PomodoroPhase { Focus, Break }

/// <summary>
/// Focus/break countdown. One scheduler timer is live only while running; it is re-armed after every tick so that
/// ticks land on whole-second boundaries of the remaining time. Remaining time is derived from the clock
/// (start instant plus remaining-at-start), so it never drifts with timer jitter. Thread-safe.
/// </summary>
public sealed class PomodoroTimer : IDisposable
{
    public const int MinMinutes = 1;
    public const int MaxMinutes = 120;

    private readonly IIslandScheduler _scheduler;
    private readonly Func<IslandSettings> _settings;
    private readonly Func<DateTimeOffset> _clock;

    // Guards all fields below. Events are raised after the lock is released.
    private readonly object _gate = new();
    private PomodoroPhase _phase = PomodoroPhase.Focus;
    private TimeSpan _phaseDuration;
    // Remaining time when last paused or reset; while running, the countdown is measured from _startedAt.
    private TimeSpan _remainingAtStart;
    private DateTimeOffset _startedAt;
    private bool _running;
    private bool _controlsLocked;
    private IDisposable? _tick;
    // Bumped whenever the live timer is cancelled or replaced, so a callback that was already dispatched does nothing.
    private int _generation;
    private bool _disposed;

    public PomodoroTimer(IIslandScheduler scheduler, Func<IslandSettings> settings, Func<DateTimeOffset>? clock = null)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        _phaseDuration = FocusDuration(_settings());
        _remainingAtStart = _phaseDuration;
    }

    public PomodoroPhase Phase
    {
        get
        {
            lock (_gate) return _phase;
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _running;
        }
    }

    public TimeSpan Remaining
    {
        get
        {
            lock (_gate) return RemainingLocked(_clock());
        }
    }

    public TimeSpan PhaseDuration
    {
        get
        {
            lock (_gate) return _phaseDuration;
        }
    }

    /// <summary>When true, Pause, Reset, SetPhase and SetMinutes are silent no-ops; Start and natural completion still work.</summary>
    public bool ControlsLocked
    {
        get
        {
            lock (_gate) return _controlsLocked;
        }
        set
        {
            lock (_gate) _controlsLocked = value;
        }
    }

    /// <summary>Raised on every tick (about once per second while running) and after every control call. Arbitrary thread.</summary>
    public event Action? Changed;

    /// <summary>Raised once when the remaining time reaches zero; argument is the phase that just ended. The timer then switches to the other phase, paused.</summary>
    public event Action<PomodoroPhase>? PhaseCompleted;

    /// <summary>Starts counting down. No-op while already running.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (!_running)
            {
                _running = true;
                _startedAt = _clock();
                ArmLocked(DelayFor(_remainingAtStart));
            }
        }

        RaiseChanged();
    }

    /// <summary>Stops counting down and keeps the remaining time.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed || _controlsLocked) return;
            if (_running)
            {
                _remainingAtStart = RemainingLocked(_clock());
                _running = false;
                CancelTimerLocked();
            }
        }

        RaiseChanged();
    }

    public void Toggle()
    {
        bool running;
        lock (_gate) running = _running;
        if (running) Pause();
        else Start();
    }

    /// <summary>Stops and restores the full duration of the current phase.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            if (_disposed || _controlsLocked) return;
            StopLocked();
            _remainingAtStart = _phaseDuration;
        }

        RaiseChanged();
    }

    /// <summary>Stops and loads the configured duration of <paramref name="phase"/>.</summary>
    public void SetPhase(PomodoroPhase phase)
    {
        var settings = _settings();
        lock (_gate)
        {
            if (_disposed || _controlsLocked) return;
            StopLocked();
            _phase = phase;
            _phaseDuration = DurationFor(phase, settings);
            _remainingAtStart = _phaseDuration;
        }

        RaiseChanged();
    }

    /// <summary>Sets the duration of the CURRENT phase in minutes (1..120) and resets remaining to it. Does not change settings.</summary>
    public void SetMinutes(int minutes)
    {
        var clamped = TimeSpan.FromMinutes(Math.Clamp(minutes, MinMinutes, MaxMinutes));
        lock (_gate)
        {
            if (_disposed || _controlsLocked) return;
            StopLocked();
            _phaseDuration = clamped;
            _remainingAtStart = clamped;
        }

        RaiseChanged();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
            CancelTimerLocked();
        }
    }

    private void OnTick(int generation)
    {
        var settings = _settings();
        PomodoroPhase? completed = null;

        lock (_gate)
        {
            if (_disposed || !_running || generation != _generation) return;

            // The fired timer is spent; replace it below if the countdown continues.
            _tick?.Dispose();
            _tick = null;

            var remaining = RemainingLocked(_clock());
            if (remaining > TimeSpan.Zero)
            {
                ArmLocked(DelayFor(remaining));
            }
            else
            {
                var ended = _phase;
                _phase = ended == PomodoroPhase.Focus ? PomodoroPhase.Break : PomodoroPhase.Focus;
                _phaseDuration = _phase == PomodoroPhase.Focus ? FocusDuration(settings) : BreakDuration(settings);
                _remainingAtStart = _phaseDuration;
                _running = false;
                CancelTimerLocked();
                completed = ended;
            }
        }

        RaiseChanged();
        if (completed is { } phase) PhaseCompleted?.Invoke(phase);
    }

    /// <summary>Time left at the clock's current instant. Caller holds the lock.</summary>
    private TimeSpan RemainingLocked(DateTimeOffset now)
    {
        if (!_running) return _remainingAtStart;
        var left = _remainingAtStart - (now - _startedAt);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>
    /// Delay to the next whole-second boundary of the remaining time, so each tick lands on a second
    /// (for example 24.3 s remaining: wake after 0.3 s, then every second). Caller holds the lock.
    /// </summary>
    private static TimeSpan DelayFor(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return TimeSpan.Zero;
        var whole = TimeSpan.FromTicks(remaining.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond);
        var fraction = remaining - whole;
        return fraction > TimeSpan.Zero ? fraction : TimeSpan.FromSeconds(1);
    }

    /// <summary>Cancels the countdown, keeping the phase and remaining time as they are. Caller holds the lock.</summary>
    private void StopLocked()
    {
        if (_running) _remainingAtStart = RemainingLocked(_clock());
        _running = false;
        CancelTimerLocked();
    }

    // Caller holds the lock. Replaces the live timer; the generation bump makes any dispatched callback of the old one a no-op.
    private void ArmLocked(TimeSpan delay)
    {
        CancelTimerLocked();
        var generation = _generation;
        _tick = _scheduler.Schedule(delay, () => OnTick(generation));
    }

    private void CancelTimerLocked()
    {
        _generation++;
        var tick = _tick;
        _tick = null;
        tick?.Dispose();
    }

    private void RaiseChanged() => Changed?.Invoke();

    private static TimeSpan FocusDuration(IslandSettings s) => TimeSpan.FromMinutes(ClampSettingMinutes(s.PomodoroFocusMinutes));

    private static TimeSpan BreakDuration(IslandSettings s) => TimeSpan.FromMinutes(ClampSettingMinutes(s.PomodoroBreakMinutes));

    private static TimeSpan DurationFor(PomodoroPhase phase, IslandSettings s) =>
        phase == PomodoroPhase.Focus ? FocusDuration(s) : BreakDuration(s);

    // A zero or negative setting would complete immediately; clamp to the same range as SetMinutes.
    private static int ClampSettingMinutes(int minutes) => Math.Clamp(minutes, MinMinutes, MaxMinutes);
}

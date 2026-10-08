using Island.Core.Abstractions;
using Island.Core.Configuration;

namespace Island.Core.Pomodoro;

public enum PomodoroPhase { Focus, Break, Prep }

/// <summary>
/// Focus/break countdown, plus a fixed <see cref="PrepMinutes"/> preparation phase before studying.
/// One scheduler timer is live only while running; it is re-armed after every tick so that
/// ticks land on whole-second boundaries of the remaining time. Remaining time is derived from the clock
/// (start instant plus remaining-at-start), so it never drifts with timer jitter. Thread-safe.
/// A plan (<see cref="StartPlan"/>) runs a number of focus/break pairs back to back: each natural phase end starts
/// the next phase by itself until the last break ends. Without a plan a phase end just pauses on the next phase.
/// </summary>
public sealed class PomodoroTimer : IDisposable
{
    public const int MinMinutes = 1;
    public const int MaxMinutes = 120;
    public const int MinCycles = 1;
    public const int MaxCycles = 12;
    /// <summary>Length of the Pré phase. Fixed: it is not a setting.</summary>
    public const int PrepMinutes = 5;

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
    // Pomodoros in the active plan (0 = no plan) and the 1-based pomodoro currently running in it.
    private int _planTotal;
    private int _planCycle;
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

    /// <summary>True while a plan is active, including while it is paused.</summary>
    public bool PlanActive
    {
        get
        {
            lock (_gate) return _planTotal > 0;
        }
    }

    /// <summary>1-based pomodoro of the active plan; 0 when no plan is active.</summary>
    public int Cycle
    {
        get
        {
            lock (_gate) return _planCycle;
        }
    }

    /// <summary>Pomodoros in the active plan; 0 when no plan is active.</summary>
    public int TotalCycles
    {
        get
        {
            lock (_gate) return _planTotal;
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

    /// <summary>Raised once when the remaining time reaches zero; argument is the phase that just ended. The timer then switches to the other phase, paused unless a plan carries on.</summary>
    public event Action<PomodoroPhase>? PhaseCompleted;

    /// <summary>Raised after <see cref="PhaseCompleted"/> on every natural phase end, with or without a plan. Arbitrary thread.</summary>
    public event Action<PomodoroTransition>? Transitioned;

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

    /// <summary>
    /// Starts a plan of <paramref name="cycles"/> focus/break pairs (clamped to <see cref="MinCycles"/>..<see cref="MaxCycles"/>).
    /// A fresh focus starts counting at once and every later phase starts by itself. No-op while controls are locked.
    /// </summary>
    public void StartPlan(int cycles)
    {
        var settings = _settings();
        lock (_gate)
        {
            if (_disposed || _controlsLocked) return;
            StopLocked();
            _phase = PomodoroPhase.Focus;
            _phaseDuration = FocusDuration(settings);
            _remainingAtStart = _phaseDuration;
            _planTotal = Math.Clamp(cycles, MinCycles, MaxCycles);
            _planCycle = 1;
            _running = true;
            _startedAt = _clock();
            ArmLocked(DelayFor(_remainingAtStart));
        }

        RaiseChanged();
    }

    /// <summary>Stops counting down and keeps the remaining time. A plan stays active.</summary>
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

    /// <summary>
    /// The main button. Running: pauses. Paused in the middle of a phase or inside a plan: resumes.
    /// Fresh focus with no plan: starts a plan of <paramref name="cycles"/> pomodoros. Any other fresh phase: starts it alone.
    /// </summary>
    public void Play(int cycles)
    {
        bool running;
        bool freshFocus;
        lock (_gate)
        {
            running = _running;
            freshFocus = !_running && _planTotal == 0 && _phase == PomodoroPhase.Focus && _remainingAtStart == _phaseDuration;
        }

        if (running) Pause();
        else if (freshFocus) StartPlan(cycles);
        else Start();
    }

    /// <summary>Stops and restores the full duration of the current phase. With a plan active, clears it and returns to a fresh focus.</summary>
    public void Reset()
    {
        var settings = _settings();
        lock (_gate)
        {
            if (_disposed || _controlsLocked) return;
            StopLocked();
            if (_planTotal > 0)
            {
                ClearPlanLocked();
                _phase = PomodoroPhase.Focus;
                _phaseDuration = FocusDuration(settings);
            }
            _remainingAtStart = _phaseDuration;
        }

        RaiseChanged();
    }

    /// <summary>Stops and loads the configured duration of <paramref name="phase"/>. Clears any plan.</summary>
    public void SetPhase(PomodoroPhase phase)
    {
        var settings = _settings();
        lock (_gate)
        {
            if (_disposed || _controlsLocked) return;
            StopLocked();
            ClearPlanLocked();
            _phase = phase;
            _phaseDuration = DurationFor(phase, settings);
            _remainingAtStart = _phaseDuration;
        }

        RaiseChanged();
    }

    /// <summary>Sets the duration of the CURRENT phase in minutes (1..120) and resets remaining to it. Does not change settings. Clears any plan.</summary>
    public void SetMinutes(int minutes)
    {
        var clamped = TimeSpan.FromMinutes(Math.Clamp(minutes, MinMinutes, MaxMinutes));
        lock (_gate)
        {
            if (_disposed || _controlsLocked) return;
            StopLocked();
            ClearPlanLocked();
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
        PomodoroTransition? transition = null;

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
                transition = CompletePhaseLocked(settings);
            }
        }

        RaiseChanged();
        if (transition is { } t)
        {
            PhaseCompleted?.Invoke(t.Ended);
            Transitioned?.Invoke(t);
        }
    }

    /// <summary>
    /// Switches to the next phase when the countdown reaches zero. Without a plan the timer stops on the next phase.
    /// With a plan, a focus starts its break and a break starts the next focus, both by themselves; the last break
    /// ends the plan and the timer stops on a fresh focus. Caller holds the lock.
    /// </summary>
    private PomodoroTransition CompletePhaseLocked(IslandSettings settings)
    {
        var ended = _phase;
        var cycle = _planCycle;
        var total = _planTotal;

        if (total == 0)
        {
            _phase = ended == PomodoroPhase.Focus ? PomodoroPhase.Break : PomodoroPhase.Focus;
            _phaseDuration = DurationFor(_phase, settings);
            _remainingAtStart = _phaseDuration;
            _running = false;
            CancelTimerLocked();
            return new PomodoroTransition(ended, _phase, 0, 0, false, false, _phaseDuration);
        }

        if (ended == PomodoroPhase.Break && cycle >= total)
        {
            ClearPlanLocked();
            _phase = PomodoroPhase.Focus;
            _phaseDuration = FocusDuration(settings);
            _remainingAtStart = _phaseDuration;
            _running = false;
            CancelTimerLocked();
            return new PomodoroTransition(ended, _phase, cycle, total, true, false, _phaseDuration);
        }

        if (ended == PomodoroPhase.Focus)
        {
            _phase = PomodoroPhase.Break;
        }
        else
        {
            _planCycle = cycle + 1;
            _phase = PomodoroPhase.Focus;
        }
        _phaseDuration = DurationFor(_phase, settings);
        _remainingAtStart = _phaseDuration;
        // Still running: re-arm for the next phase instead of cancelling.
        _startedAt = _clock();
        ArmLocked(DelayFor(_remainingAtStart));
        return new PomodoroTransition(ended, _phase, cycle, total, false, true, _phaseDuration);
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

    private void ClearPlanLocked()
    {
        _planTotal = 0;
        _planCycle = 0;
    }

    private void RaiseChanged() => Changed?.Invoke();

    private static TimeSpan FocusDuration(IslandSettings s) => TimeSpan.FromMinutes(ClampSettingMinutes(s.PomodoroFocusMinutes));

    private static TimeSpan BreakDuration(IslandSettings s) => TimeSpan.FromMinutes(ClampSettingMinutes(s.PomodoroBreakMinutes));

    private static TimeSpan DurationFor(PomodoroPhase phase, IslandSettings s) => phase switch
    {
        PomodoroPhase.Focus => FocusDuration(s),
        PomodoroPhase.Break => BreakDuration(s),
        _ => TimeSpan.FromMinutes(PrepMinutes),
    };

    // A zero or negative setting would complete immediately; clamp to the same range as SetMinutes.
    private static int ClampSettingMinutes(int minutes) => Math.Clamp(minutes, MinMinutes, MaxMinutes);
}

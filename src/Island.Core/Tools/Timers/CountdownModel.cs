namespace Island.Core.Tools.Timers;

/// <summary>
/// Countdown with presets and fine adjustment. Remaining time comes from the clock. One <c>System.Threading.Timer</c>
/// is armed for the instant the countdown ends, not a one-second tick; pausing, resetting or changing the duration
/// disarms it. <see cref="Tick"/> runs on a thread-pool thread, so subscribers must marshal to the UI themselves.
/// </summary>
public sealed class CountdownModel : IDisposable
{
    public static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(180);

    public static IReadOnlyList<TimeSpan> Presets { get; } = Array.AsReadOnly(
        new[] { 1, 3, 5, 10, 15, 25, 45, 60 }.Select(minutes => TimeSpan.FromMinutes(minutes)).ToArray());

    private readonly TimeProvider _clock;
    private readonly Action? _alarm;
    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new();
    private TimeSpan _duration = TimeSpan.FromMinutes(5);
    // Remaining time when stopped; while running, the time left at _startedAt.
    private TimeSpan _remaining = TimeSpan.FromMinutes(5);
    private DateTimeOffset _startedAt;
    private CountdownState _state = CountdownState.Idle;
    private bool _disposed;

    /// <param name="alarm">Called once when the countdown ends (the sound), kept apart from the state so tests can count it.</param>
    public CountdownModel(TimeProvider? clock = null, Action? alarm = null)
    {
        _clock = clock ?? TimeProvider.System;
        _alarm = alarm;
        _timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Raised on every state or duration change. Not raised per frame.</summary>
    public event Action? Changed;

    /// <summary>Raised exactly once per run, when the time runs out.</summary>
    public event Action? Finished;

    public TimeSpan Duration
    {
        get
        {
            lock (_gate) return _duration;
        }
    }

    public CountdownState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    public bool IsRunning => State == CountdownState.Running;

    public TimeSpan Remaining
    {
        get
        {
            lock (_gate) return RemainingAt(_clock.GetUtcNow());
        }
    }

    /// <summary>Share of the duration that has elapsed, from 0 (just started) to 1 (finished).</summary>
    public double Progress
    {
        get
        {
            lock (_gate)
            {
                if (_state == CountdownState.Finished) return 1;
                TimeSpan elapsed = _duration - RemainingAt(_clock.GetUtcNow());
                return Math.Clamp(elapsed.Ticks / (double)_duration.Ticks, 0, 1);
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _state == CountdownState.Running) return;

            if (_state == CountdownState.Finished) _remaining = _duration;
            _startedAt = _clock.GetUtcNow();
            _state = CountdownState.Running;
            _timer.Change(_remaining, Timeout.InfiniteTimeSpan);
        }
        Changed?.Invoke();
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed || _state != CountdownState.Running) return;

            _remaining = RemainingAt(_clock.GetUtcNow());
            _state = CountdownState.Paused;
            Disarm();
        }
        Changed?.Invoke();
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (_disposed || (_state == CountdownState.Idle && _remaining == _duration)) return;

            ReturnToIdle();
        }
        Changed?.Invoke();
    }

    /// <summary>Moves to the next preset longer than the current duration, wrapping after the last one.</summary>
    public void CyclePreset()
    {
        lock (_gate)
        {
            if (_disposed || _state == CountdownState.Running) return;

            TimeSpan next = Presets.FirstOrDefault(preset => preset > _duration, Presets[0]);
            if (next == _duration) return;

            _duration = next;
            ReturnToIdle();
        }
        Changed?.Invoke();
    }

    /// <summary>Changes the duration while not running. A paused countdown starts over at the new duration.</summary>
    public void Adjust(TimeSpan delta)
    {
        lock (_gate)
        {
            if (_disposed || _state == CountdownState.Running) return;

            TimeSpan next = TimeSpan.FromTicks(Math.Clamp((_duration + delta).Ticks, MinDuration.Ticks, MaxDuration.Ticks));
            if (next == _duration) return;

            _duration = next;
            ReturnToIdle();
        }
        Changed?.Invoke();
    }

    /// <summary>Called by the scheduled timer at the end time. Also safe to call early: it re-arms until the time is up.</summary>
    public void Tick()
    {
        lock (_gate)
        {
            if (_disposed || _state != CountdownState.Running) return;

            TimeSpan remaining = RemainingAt(_clock.GetUtcNow());
            if (remaining > TimeSpan.Zero)
            {
                _timer.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }

            _remaining = TimeSpan.Zero;
            _state = CountdownState.Finished;
            Disarm();
        }

        _alarm?.Invoke();
        Changed?.Invoke();
        Finished?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
        }
    }

    private TimeSpan RemainingAt(DateTimeOffset now)
    {
        if (_state != CountdownState.Running) return _remaining;

        TimeSpan left = _remaining - (now - _startedAt);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    private void ReturnToIdle()
    {
        _state = CountdownState.Idle;
        _remaining = _duration;
        Disarm();
    }

    private void Disarm() => _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
}

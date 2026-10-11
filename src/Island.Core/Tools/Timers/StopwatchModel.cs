namespace Island.Core.Tools.Timers;

/// <summary>
/// Stopwatch with laps. Elapsed time is read from the clock (the instant of the last start plus the time banked by
/// earlier runs), so a late UI frame never makes it drift. <see cref="Changed"/> fires on start, pause, lap and reset
/// only, never per frame: the widget polls <see cref="Elapsed"/> while it draws.
/// </summary>
public sealed class StopwatchModel
{
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly List<LapRecord> _laps = new();
    private bool _running;
    private DateTimeOffset _startedAt;
    private TimeSpan _banked;
    private TimeSpan _lastLapTotal;

    public StopwatchModel(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public event Action? Changed;

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _running;
        }
    }

    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate) return ElapsedAt(_clock.GetUtcNow());
        }
    }

    /// <summary>Laps, most recent first.</summary>
    public IReadOnlyList<LapRecord> Laps
    {
        get
        {
            lock (_gate) return _laps.AsEnumerable().Reverse().ToList();
        }
    }

    /// <summary>The fastest lap, or null when there is nothing to compare (fewer than two laps, or all tied).</summary>
    public LapRecord? BestLap => Extremes().Best;

    /// <summary>The slowest lap, with the same rules as <see cref="BestLap"/>.</summary>
    public LapRecord? WorstLap => Extremes().Worst;

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            _startedAt = _clock.GetUtcNow();
            _running = true;
        }
        Changed?.Invoke();
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (!_running) return;
            _banked = ElapsedAt(_clock.GetUtcNow());
            _running = false;
        }
        Changed?.Invoke();
    }

    /// <summary>Records a lap. Ignored while stopped, so a paused stopwatch cannot add empty laps.</summary>
    public void Lap()
    {
        lock (_gate)
        {
            if (!_running) return;
            TimeSpan total = ElapsedAt(_clock.GetUtcNow());
            _laps.Add(new LapRecord(_laps.Count + 1, total - _lastLapTotal, total));
            _lastLapTotal = total;
        }
        Changed?.Invoke();
    }

    public void Reset()
    {
        lock (_gate)
        {
            bool hadState = _running || _banked > TimeSpan.Zero || _laps.Count > 0;
            if (!hadState) return;

            _running = false;
            _banked = TimeSpan.Zero;
            _lastLapTotal = TimeSpan.Zero;
            _laps.Clear();
        }
        Changed?.Invoke();
    }

    private TimeSpan ElapsedAt(DateTimeOffset now) => _running ? _banked + (now - _startedAt) : _banked;

    private (LapRecord? Best, LapRecord? Worst) Extremes()
    {
        lock (_gate)
        {
            if (_laps.Count < 2) return (null, null);

            LapRecord best = _laps.MinBy(lap => lap.LapTime)!;
            LapRecord worst = _laps.MaxBy(lap => lap.LapTime)!;
            return best.LapTime == worst.LapTime ? (null, null) : (best, worst);
        }
    }
}

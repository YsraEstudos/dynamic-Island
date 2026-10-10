using Island.Core.Abstractions;

namespace Island.Core.Idle;

/// <summary>
/// Keeps the latest weather reading fresh while the island is idle, and does nothing else. A single timer runs only
/// while <see cref="SetIdle"/> is true: at most one reading per <see cref="WeatherRefreshPolicy.Interval"/>, and a failed
/// read is retried after <see cref="WeatherRefreshPolicy.FailureBackoff"/>. Leaving idle cancels the timer.
/// The last reading and its place go to <see cref="IWeatherCache"/>, so a restart shows something without the network.
/// Thread-safe. Events are raised outside the internal lock.
/// </summary>
public sealed class WeatherRefresher : IDisposable
{
    private readonly IWeatherService _service;
    private readonly IWeatherCache _cache;
    private readonly IIslandScheduler _scheduler;
    private readonly Func<DateTimeOffset> _clock;
    private readonly CancellationTokenSource _cancel = new();

    // Guards the fields below. Service calls and cache writes happen outside it.
    private readonly object _gate = new();
    private WeatherCacheData _data;
    private string _city;
    private DateTimeOffset? _lastFailure;
    private bool _idle;
    private bool _inFlight;
    private IDisposable? _timer;
    // Bumped whenever the timer is armed or cancelled, so a callback dispatched earlier does nothing.
    private int _generation;
    private bool _disposed;

    public WeatherRefresher(IWeatherService service, IWeatherCache cache, IIslandScheduler scheduler, Func<DateTimeOffset> clock)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        _data = LoadSafe(cache);
        // The cached reading belongs to the city it was read for, so the city setting starts from that one.
        _city = _data.Place?.Query ?? string.Empty;
    }

    /// <summary>The last successful reading for the current city, or null.</summary>
    public WeatherSnapshot? Current
    {
        get
        {
            lock (_gate) return _data.Snapshot;
        }
    }

    /// <summary>Raised when <see cref="Current"/> changes. Arbitrary thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// Sets the city to read ("" = approximate location). Idempotent. A different city drops the old reading and,
    /// when idle, reads again at once.
    /// </summary>
    public void SetCity(string city)
    {
        string normalized = (city ?? string.Empty).Trim();
        lock (_gate)
        {
            if (_disposed || normalized == _city) return;
            _city = normalized;
            _data = _data with { Snapshot = null };
            _lastFailure = null;
            if (_idle && !_inFlight) ArmLocked(DelayLocked());
        }

        RaiseChanged();
    }

    /// <summary>Starts or stops the refresh cycle. Entering idle arms the timer for the next due attempt; leaving it cancels the timer.</summary>
    public void SetIdle(bool idle)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _idle = idle;
            if (!idle)
            {
                CancelTimerLocked();
                return;
            }

            if (_timer is null && !_inFlight) ArmLocked(DelayLocked());
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _idle = false;
            CancelTimerLocked();
        }

        // Not disposed: a read still in flight may use the token after this point.
        _cancel.Cancel();
    }

    private void OnDue(int generation)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation || !_idle || _inFlight) return;
            _timer = null;
            _inFlight = true;
        }

        _ = AttemptAsync();
    }

    private async Task AttemptAsync()
    {
        string city;
        WeatherPlace? known;
        lock (_gate)
        {
            city = _city;
            known = _data.Place is { } place && place.Query == city ? place : null;
        }

        WeatherPlace? resolved = known;
        WeatherSnapshot? snapshot = null;
        try
        {
            resolved ??= await _service.ResolvePlaceAsync(city, _cancel.Token).ConfigureAwait(false);
            if (resolved is not null)
            {
                snapshot = await _service.FetchAsync(resolved, _cancel.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // A network or parse failure (or a cancel on dispose) is a failed attempt, never an error the island shows.
            snapshot = null;
        }

        WeatherCacheData toSave;
        bool changed = false;
        lock (_gate)
        {
            _inFlight = false;
            if (_disposed) return;

            // The city can change while the read is out: then the result is for the old city and is dropped.
            bool currentCity = city == _city;
            if (resolved is not null && resolved != known) _data = _data with { Place = resolved };
            if (currentCity)
            {
                if (snapshot is not null)
                {
                    _data = _data with { Snapshot = snapshot };
                    _lastFailure = null;
                    changed = true;
                }
                else
                {
                    _lastFailure = _clock();
                }
            }

            toSave = _data;
            if (_idle) ArmLocked(DelayLocked());
        }

        SaveSafe(toSave);
        if (changed) RaiseChanged();
    }

    // Caller holds the lock.
    private TimeSpan DelayLocked() =>
        WeatherRefreshPolicy.DelayUntilNextAttempt(_clock(), _data.Snapshot?.FetchedAt, _lastFailure);

    // Caller holds the lock. Replaces any armed timer.
    private void ArmLocked(TimeSpan delay)
    {
        CancelTimerLocked();
        int generation = _generation;
        _timer = _scheduler.Schedule(delay, () => OnDue(generation));
    }

    // Caller holds the lock.
    private void CancelTimerLocked()
    {
        _generation++;
        IDisposable? timer = _timer;
        _timer = null;
        timer?.Dispose();
    }

    private void RaiseChanged() => Changed?.Invoke();

    private WeatherCacheData LoadSafe(IWeatherCache cache)
    {
        try
        {
            return cache.Load() ?? WeatherCacheData.Empty;
        }
        catch (Exception)
        {
            // A cache that cannot be read is the same as an empty one.
            return WeatherCacheData.Empty;
        }
    }

    private void SaveSafe(WeatherCacheData data)
    {
        try
        {
            _cache.Save(data);
        }
        catch (Exception)
        {
            // The reading is still in memory; only the restart shortcut is lost.
        }
    }
}

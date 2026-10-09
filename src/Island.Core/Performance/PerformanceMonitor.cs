using System.Diagnostics;
using Island.Core.Abstractions;

namespace Island.Core.Performance;

/// <summary>
/// Samples the machine only while something needs the data: the widget is on screen (every 1 s) or temperature
/// alerts are on (every 3 s). Otherwise no timer is armed. Exactly one timer is pending at a time, re-armed after each
/// sample. Samples run on the scheduler's thread-pool thread, never on the UI thread, and the events are raised from
/// that thread: subscribers must marshal to the UI themselves.
/// A timer callback must never throw (that would end the process), so every failure inside a sample is contained.
/// </summary>
public sealed class PerformanceMonitor : IDisposable
{
    private readonly IPerformanceSampler _sampler;
    private readonly IPerformanceSettingsStore _store;
    private readonly IIslandScheduler _scheduler;
    private readonly Func<TimeSpan> _clock;
    private readonly TemperatureAlertPolicy _alertPolicy = new();
    private readonly PerformanceHistory _history = new();

    // Guards the fields below. Never held while sampling, saving or raising events.
    private readonly object _gate = new();
    private PerformanceAlertSettings _alerts;
    private bool _widgetVisible;
    private bool _started;
    private bool _disposed;
    private bool _sampling;
    private int _generation;
    private IDisposable? _timer;
    private PerformanceSnapshot? _latest;

    /// <param name="clock">Monotonic time for the alert cooldown. Defaults to a stopwatch; tests pass virtual time.</param>
    public PerformanceMonitor(IPerformanceSampler sampler, IPerformanceSettingsStore store, IIslandScheduler scheduler,
        Func<TimeSpan>? clock = null)
    {
        _sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _clock = clock ?? CreateStopwatchClock();
        _alerts = store.Load().Sanitized();
    }

    /// <summary>Raised after every sample, from the sampling thread.</summary>
    public event Action<PerformanceSnapshot>? SnapshotChanged;

    /// <summary>Raised when a sensor crosses its limit, from the sampling thread.</summary>
    public event Action<TemperatureAlert>? AlertRaised;

    public PerformanceSnapshot? Latest
    {
        get
        {
            lock (_gate) return _latest;
        }
    }

    public PerformanceAlertSettings Alerts
    {
        get
        {
            lock (_gate) return _alerts;
        }
    }

    public double?[] CpuHistory() => _history.Cpu();

    public double?[] GpuHistory() => _history.Gpu();

    /// <summary>Arms sampling for the current needs. Idempotent. With no needs, nothing is armed.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
            RescheduleLocked(immediate: false);
        }
    }

    /// <summary>The widget calls this when it appears on or leaves the screen. Showing it samples at once.</summary>
    public void SetWidgetVisible(bool visible)
    {
        lock (_gate)
        {
            if (_disposed || _widgetVisible == visible) return;
            _widgetVisible = visible;
            if (_started) RescheduleLocked(immediate: visible);
        }
    }

    /// <summary>Applies and saves new alert settings. Limits are clamped; an unchanged value is not saved again.</summary>
    public void SetAlerts(PerformanceAlertSettings settings)
    {
        PerformanceAlertSettings clean = settings.Sanitized();
        lock (_gate)
        {
            if (_disposed || clean == _alerts) return;
            if (clean.Enabled != _alerts.Enabled) _alertPolicy.Reset();
            _alerts = clean;
            if (_started) RescheduleLocked(immediate: false);
        }

        // The value is already in effect; a failed save only means it will not survive a restart.
        try { _store.Save(clean); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>
    /// Caller holds the gate. Replaces the pending timer with one for the current needs. While a sample runs, the timer
    /// is left alone: the sample re-arms when it finishes, with the needs it sees then.
    /// </summary>
    private void RescheduleLocked(bool immediate)
    {
        _generation++;
        _timer?.Dispose();
        _timer = null;
        if (_disposed || _sampling) return;

        TimeSpan? interval = PerformanceSamplingPolicy.IntervalFor(_widgetVisible, _alerts.Enabled);
        if (interval is not { } wait) return;

        int generation = _generation;
        _timer = _scheduler.Schedule(immediate ? TimeSpan.Zero : wait, () => OnTimer(generation));
    }

    private void OnTimer(int generation)
    {
        lock (_gate)
        {
            // A timer replaced by a later reschedule must do nothing.
            if (_disposed || generation != _generation || _sampling) return;
            _timer = null;
            _sampling = true;
        }

        try
        {
            SampleAndPublish();
        }
        finally
        {
            lock (_gate)
            {
                _sampling = false;
                if (!_disposed) RescheduleLocked(immediate: false);
            }
        }
    }

    private void SampleAndPublish()
    {
        PerformanceSnapshot snapshot;
        try
        {
            snapshot = _sampler.Sample();
        }
        catch (Exception)
        {
            // Skipped: the next tick reads again. The sampler reports missing sensors as nulls, so this is a bug guard.
            return;
        }

        IReadOnlyList<TemperatureAlert> alerts;
        lock (_gate)
        {
            if (_disposed) return;
            _latest = snapshot;
            _history.Add(snapshot);
            alerts = _alertPolicy.Evaluate(snapshot, _alerts, _clock());
        }

        Notify(() => SnapshotChanged?.Invoke(snapshot));
        foreach (TemperatureAlert alert in alerts) Notify(() => AlertRaised?.Invoke(alert));
    }

    /// <summary>A subscriber's failure must not stop the sampling timer.</summary>
    private static void Notify(Action raise)
    {
        try { raise(); }
        catch (Exception) { }
    }

    private static Func<TimeSpan> CreateStopwatchClock()
    {
        var watch = Stopwatch.StartNew();
        return () => watch.Elapsed;
    }
}

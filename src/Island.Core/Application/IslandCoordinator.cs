using Island.Core.Abstractions;
using Island.Core.Configuration;
using Island.Core.Models;

namespace Island.Core.Application;

/// <summary>
/// Single authority deciding what the island shows. Service and UI events go through
/// <see cref="IslandStateReducer"/>. One timer covers every temporary state and the expanded idle collapse;
/// a generation counter makes callbacks from cancelled timers harmless.
/// </summary>
public sealed class IslandCoordinator : IDisposable
{
    private readonly IMediaService _media;
    private readonly IVolumeService _volume;
    private readonly IDisplayService _display;
    private readonly IIslandScheduler _scheduler;
    private readonly Func<IslandSettings> _settings;

    // Guards all fields below. Never held while raising StateChanged or calling into services.
    private readonly object _gate = new();
    // States waiting to be raised, in commit order. Drained by one thread at a time (see Drain).
    private readonly Queue<IslandState> _pending = new();

    private IslandState _state = IslandState.Initial;
    private ReducerFlags _flags = ReducerFlags.None;
    private string? _lastTrackKey;
    private IDisposable? _timer;
    private int _timerGeneration;
    private bool _started;
    private bool _disposed;
    private bool _draining;

    public IslandCoordinator(IMediaService media, IVolumeService volume, IDisplayService display,
        IIslandScheduler scheduler, Func<IslandSettings> settings)
    {
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _volume = volume ?? throw new ArgumentNullException(nameof(volume));
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public IslandState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    /// <summary>
    /// Raised after every state change, from an arbitrary thread (never while holding internal locks).
    /// Emissions are serialized and in commit order; an event raised during a handler is queued, not nested.
    /// </summary>
    public event Action<IslandState>? StateChanged;

    /// <summary>Subscribes to the services and seeds state from their current values. Idempotent.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }

        _media.MediaChanged += OnMediaChanged;
        _volume.VolumeChanged += OnVolumeChanged;
        _display.FullscreenChanged += OnFullscreenChanged;

        // Read service values and settings before taking our lock: services may hold their own locks while raising events.
        var settings = _settings();
        var media = _media.Current;
        var volume = _volume.Current;
        var fullscreen = _display.IsFullscreenAppActive;

        bool drain;
        lock (_gate)
        {
            if (_disposed) return;
            // Seed the track key so an already-playing track does not count as a change.
            _lastTrackKey = media?.TrackKey;
            _flags = _flags with { Fullscreen = fullscreen };
            var seeded = _state with
            {
                // A pill left minimized last session stays minimized.
                Mode = settings.Minimized ? IslandMode.Mini : _state.Mode,
                Media = media,
                Volume = volume,
                Suspended = IslandStateReducer.IsSuspended(_flags, settings),
            };
            drain = CommitLocked(seeded);
        }

        if (drain) Drain();
    }

    /// <summary>
    /// Thread-safe entry point for UI-originated and feature-originated events (ExpandRequested, CustomizeRequested,
    /// ClipboardRequested, NoticeRaised, CollapseRequested, InteractionChanged, PausedChanged).
    /// </summary>
    public void Post(IslandEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        // The timer is owned by the coordinator; an external expiry would bypass its generation check.
        if (e is IslandEvent.TemporaryStateExpired) return;

        var settings = _settings();
        bool drain;
        lock (_gate)
        {
            drain = ApplyLocked(e, settings);
        }

        if (drain) Drain();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CancelTimerLocked();
            _pending.Clear();
        }

        _media.MediaChanged -= OnMediaChanged;
        _volume.VolumeChanged -= OnVolumeChanged;
        _display.FullscreenChanged -= OnFullscreenChanged;
    }

    private void OnMediaChanged(object? sender, MediaInfo? media)
    {
        var settings = _settings();
        bool drain;
        lock (_gate)
        {
            // First non-null media after null (or a different track key) counts as a track change.
            var trackChanged = media is not null && media.TrackKey != _lastTrackKey;
            _lastTrackKey = media?.TrackKey;
            drain = ApplyLocked(new IslandEvent.MediaChanged(media, trackChanged), settings);
        }

        if (drain) Drain();
    }

    private void OnVolumeChanged(object? sender, VolumeInfo volume)
    {
        var settings = _settings();
        bool drain;
        lock (_gate)
        {
            drain = ApplyLocked(new IslandEvent.VolumeChanged(volume), settings);
        }

        if (drain) Drain();
    }

    private void OnFullscreenChanged(object? sender, bool isFullscreen)
    {
        var settings = _settings();
        bool drain;
        lock (_gate)
        {
            drain = ApplyLocked(new IslandEvent.FullscreenChanged(isFullscreen), settings);
        }

        if (drain) Drain();
    }

    private void OnTimerFired(int generation)
    {
        var settings = _settings();
        bool drain;
        lock (_gate)
        {
            // A cancelled or superseded timer whose callback was already dispatched must do nothing.
            if (_disposed || generation != _timerGeneration) return;
            _timer?.Dispose();
            _timer = null;
            drain = ApplyLocked(new IslandEvent.TemporaryStateExpired(), settings);
        }

        if (drain) Drain();
    }

    /// <summary>Runs the reducer and applies its timer and state results. Caller holds the lock.</summary>
    /// <returns>True if the caller must call <see cref="Drain"/> after releasing the lock.</returns>
    private bool ApplyLocked(IslandEvent e, IslandSettings settings)
    {
        if (_disposed) return false;

        var result = IslandStateReducer.Reduce(_state, _flags, e, settings);
        _flags = result.Flags;

        switch (result.Timer)
        {
            case TimerAction.Arm:
                ArmTimerLocked(result.Delay);
                break;
            case TimerAction.Cancel:
                CancelTimerLocked();
                break;
        }

        return CommitLocked(result.State);
    }

    /// <summary>Stores and queues the state if it differs from the current one. Caller holds the lock.</summary>
    private bool CommitLocked(IslandState next)
    {
        if (!IslandStateReducer.AreEquivalent(_state, next))
        {
            _state = next;
            _pending.Enqueue(next);
        }

        if (_draining || _pending.Count == 0) return false;
        _draining = true;
        return true;
    }

    /// <summary>
    /// Raises queued states in order, one thread at a time, with no lock held during the handler.
    /// A thread that finds another drain in progress returns at once; the active drainer picks up its state.
    /// This keeps UI-thread callers non-blocking and avoids deadlocks with handlers that marshal to the UI thread.
    /// </summary>
    private void Drain()
    {
        while (true)
        {
            IslandState next;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    _draining = false;
                    return;
                }

                next = _pending.Dequeue();
            }

            try
            {
                StateChanged?.Invoke(next);
            }
            catch
            {
                lock (_gate) _draining = false;
                throw;
            }
        }
    }

    // Caller holds the lock. Each arm bumps the generation so a callback from a previous arm is ignored.
    private void ArmTimerLocked(TimeSpan delay)
    {
        CancelTimerLocked();
        var generation = _timerGeneration;
        _timer = _scheduler.Schedule(delay, () => OnTimerFired(generation));
    }

    private void CancelTimerLocked()
    {
        _timerGeneration++;
        var timer = _timer;
        _timer = null;
        timer?.Dispose();
    }
}

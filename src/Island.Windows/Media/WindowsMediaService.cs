using System.Threading.Channels;
using Island.Core.Abstractions;
using Island.Core.Models;
using Microsoft.Extensions.Logging;
using Windows.Media.Control;

// The Windows.* namespace is declared at file scope, outside Island.Windows.Media. Inside that namespace,
// a bare "Windows" would resolve to Island.Windows.
namespace Island.Windows.Media;

/// <summary>
/// Current media session via GlobalSystemMediaTransportControls. Event-driven, with no polling.
/// Session events feed a capacity-1 channel. A single pump task debounces the bursts and rebuilds
/// serially, so rebuilds never overlap and an older result can never overwrite a newer one.
/// </summary>
public sealed class WindowsMediaService : IMediaService
{
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(80);

    private readonly ILogger<WindowsMediaService>? _logger;
    private readonly object _gate = new();
    private readonly Channel<byte> _signals = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    // All guarded by _gate.
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private CancellationTokenSource? _pumpCts;
    private string? _thumbnailKey;   // TrackKey the cached thumbnail belongs to; avoids re-reading on each tick
    private byte[]? _thumbnail;
    private MediaInfo? _current;
    private bool _initialized;
    private bool _disposed;

    private int _sessionDirty;       // set by CurrentSessionChanged, consumed by the pump (Interlocked)

    public WindowsMediaService(ILogger<WindowsMediaService>? logger = null)
    {
        _logger = logger;
    }

    public event EventHandler<MediaInfo?>? MediaChanged;

    public MediaInfo? Current
    {
        get { lock (_gate) return _current; }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_initialized || _disposed) return;
            _initialized = true;
        }

        GlobalSystemMediaTransportControlsSessionManager manager;
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_gate) _initialized = false;
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "System media transport controls are unavailable.");
            return;
        }

        CancellationToken pumpToken;
        lock (_gate)
        {
            if (_disposed) return;
            _manager = manager;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            _pumpCts = new CancellationTokenSource();
            pumpToken = _pumpCts.Token;
        }

        _ = Task.Run(() => PumpAsync(pumpToken));

        // Seed the first session without waiting for an event.
        Interlocked.Exchange(ref _sessionDirty, 1);
        Signal();
    }

    public Task PlayPauseAsync() =>
        RunControlAsync("play/pause", s => s.TryTogglePlayPauseAsync().AsTask());

    public Task NextAsync() =>
        RunControlAsync("next", s => s.TrySkipNextAsync().AsTask());

    public Task PreviousAsync() =>
        RunControlAsync("previous", s => s.TrySkipPreviousAsync().AsTask());

    public Task SeekAsync(TimeSpan position)
    {
        long ticks = Math.Max(0L, position.Ticks);
        return RunControlAsync("seek", s => s.TryChangePlaybackPositionAsync(ticks).AsTask());
    }

    private async Task RunControlAsync(string name, Func<GlobalSystemMediaTransportControlsSession, Task<bool>> operation)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_gate)
        {
            if (_disposed) return;
            session = _session;
        }
        if (session is null) return;

        try
        {
            await operation(session).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Media control '{Control}' failed.", name);
        }
    }

    private void Signal() => _signals.Writer.TryWrite(0);

    private async Task PumpAsync(CancellationToken ct)
    {
        var reader = _signals.Reader;
        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                await Task.Delay(CoalesceWindow, ct).ConfigureAwait(false);

                // Fold in everything that arrived during the window; the rebuild below sees their effects.
                while (reader.TryRead(out _)) { }

                try
                {
                    await RebuildAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One bad rebuild must not kill the pump.
                    _logger?.LogWarning(ex, "Media rebuild failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Dispose requested shutdown.
        }
    }

    private async Task RebuildAsync(CancellationToken ct)
    {
        GlobalSystemMediaTransportControlsSessionManager? manager;
        lock (_gate)
        {
            if (_disposed) return;
            manager = _manager;
        }
        if (manager is null) return;

        if (Interlocked.Exchange(ref _sessionDirty, 0) == 1)
            ReattachSession(manager.GetCurrentSession());

        GlobalSystemMediaTransportControlsSession? session;
        lock (_gate)
        {
            if (_disposed) return;
            session = _session;
        }

        MediaInfo? info = session is null ? null : await BuildAsync(session, ct).ConfigureAwait(false);

        lock (_gate)
        {
            if (_disposed) return;
            _current = info;
        }
        RaiseMediaChanged(info);
    }

    /// <summary>Moves the session subscriptions from the old session to the new one. No leaks.</summary>
    private void ReattachSession(GlobalSystemMediaTransportControlsSession? next)
    {
        lock (_gate)
        {
            if (_disposed) return;
            DetachSessionLocked();
            if (next is null) return;

            _session = next;
            next.MediaPropertiesChanged += OnSessionEvent;
            next.PlaybackInfoChanged += OnSessionEvent;
            next.TimelinePropertiesChanged += OnSessionEvent;
        }
    }

    /// <summary>Must be called with <see cref="_gate"/> held.</summary>
    private void DetachSessionLocked()
    {
        if (_session is null) return;
        _session.MediaPropertiesChanged -= OnSessionEvent;
        _session.PlaybackInfoChanged -= OnSessionEvent;
        _session.TimelinePropertiesChanged -= OnSessionEvent;
        _session = null;
    }

    private async Task<MediaInfo?> BuildAsync(GlobalSystemMediaTransportControlsSession session, CancellationToken ct)
    {
        try
        {
            var props = await session.TryGetMediaPropertiesAsync().AsTask(ct).ConfigureAwait(false);
            if (props is null || string.IsNullOrWhiteSpace(props.Title)) return null;

            string artist = props.Artist ?? string.Empty;
            string app = session.SourceAppUserModelId ?? string.Empty;

            bool playing = session.GetPlaybackInfo().PlaybackStatus
                == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            var timeline = session.GetTimelineProperties();
            var duration = timeline.EndTime - timeline.StartTime;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
            var position = Extrapolate(timeline, playing, duration);

            var probe = new MediaInfo(props.Title, artist, null, playing, position, duration, app);
            byte[]? thumbnail = await GetThumbnailAsync(props, probe.TrackKey, ct).ConfigureAwait(false);
            return probe with { Thumbnail = thumbnail };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Sessions can close mid-read; treat that as "no media".
            _logger?.LogDebug(ex, "Could not read the media session.");
            return null;
        }
    }

    /// <summary>Position as reported plus wall-clock time since the last timeline update while playing.</summary>
    private static TimeSpan Extrapolate(GlobalSystemMediaTransportControlsSessionTimelineProperties timeline,
        bool playing, TimeSpan duration)
    {
        var position = timeline.Position;
        if (playing)
        {
            var elapsed = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
            if (elapsed > TimeSpan.Zero) position += elapsed;
        }

        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (duration > TimeSpan.Zero && position > duration) position = duration;
        return position;
    }

    private async Task<byte[]?> GetThumbnailAsync(
        GlobalSystemMediaTransportControlsSessionMediaProperties props, string trackKey, CancellationToken ct)
    {
        lock (_gate)
        {
            if (trackKey == _thumbnailKey) return _thumbnail;
        }

        byte[]? bytes = null;
        if (props.Thumbnail is { } reference)
        {
            try
            {
                using var winrtStream = await reference.OpenReadAsync().AsTask(ct).ConfigureAwait(false);
                using var managed = winrtStream.AsStreamForRead();
                using var buffer = new MemoryStream();
                await managed.CopyToAsync(buffer, ct).ConfigureAwait(false);
                bytes = buffer.ToArray();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Could not read the media thumbnail.");
            }
        }

        // Cache the result, including a failed read, so a missing thumbnail is not retried on every tick.
        lock (_gate)
        {
            _thumbnailKey = trackKey;
            _thumbnail = bytes;
        }
        return bytes;
    }

    private void OnCurrentSessionChanged(object? sender, CurrentSessionChangedEventArgs e)
    {
        Interlocked.Exchange(ref _sessionDirty, 1);
        Signal();
    }

    /// <summary>Shared handler for MediaPropertiesChanged, PlaybackInfoChanged and TimelinePropertiesChanged.</summary>
    private void OnSessionEvent(object? sender, object args) => Signal();

    private void RaiseMediaChanged(MediaInfo? info)
    {
        try
        {
            MediaChanged?.Invoke(this, info);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "MediaChanged handler threw.");
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_manager is not null)
            {
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _manager = null;
            }
            DetachSessionLocked();
            _current = null;
            cts = _pumpCts;
            _pumpCts = null;
        }

        // Only cancel. The pump still observes the token, and disposing a CTS in use can throw.
        cts?.Cancel();
        _signals.Writer.TryComplete();
    }
}

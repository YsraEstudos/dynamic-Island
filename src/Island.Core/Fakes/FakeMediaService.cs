using Island.Core.Abstractions;
using Island.Core.Models;

namespace Island.Core.Fakes;

/// <summary>
/// In-memory <see cref="IMediaService"/>. Transport methods change the fake session and raise <see cref="MediaChanged"/>
/// synchronously on the calling thread. Track identity comes from <see cref="MediaInfo.TrackKey"/>, so the coordinator
/// derives TrackChanged itself, as it does for real services.
/// </summary>
public sealed class FakeMediaService : IMediaService
{
    private readonly object _gate = new();
    private readonly List<MediaInfo> _playlist = new();
    private int _index = -1;
    private MediaInfo? _current;

    public event EventHandler<MediaInfo?>? MediaChanged;

    public MediaInfo? Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    public int PlayPauseCount { get; private set; }
    public int NextCount { get; private set; }
    public int PreviousCount { get; private set; }
    public int SeekCount { get; private set; }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Replaces the current session directly. Pass null to simulate "no session".</summary>
    public void SetMedia(MediaInfo? media)
    {
        lock (_gate)
        {
            _current = media;
        }

        Raise(media);
    }

    /// <summary>Sets the playlist used by Next/Previous and makes the first track current.</summary>
    public void SetPlaylist(params MediaInfo[] tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        lock (_gate)
        {
            _playlist.Clear();
            _playlist.AddRange(tracks);
            _index = _playlist.Count > 0 ? 0 : -1;
            _current = _playlist.Count > 0 ? _playlist[0] : null;
        }

        Raise(Current);
    }

    /// <summary>Advances the position by <paramref name="elapsed"/> without changing the track: a playback tick.</summary>
    public void Tick(TimeSpan elapsed)
    {
        MediaInfo? updated;
        lock (_gate)
        {
            if (_current is null) return;
            var position = _current.Position + elapsed;
            if (position > _current.Duration) position = _current.Duration;
            _current = _current with { Position = position };
            updated = _current;
        }

        Raise(updated);
    }

    public Task PlayPauseAsync()
    {
        PlayPauseCount++;
        MediaInfo? updated = null;
        lock (_gate)
        {
            if (_current is not null)
            {
                _current = _current with { IsPlaying = !_current.IsPlaying };
                updated = _current;
            }
        }

        if (updated is not null) Raise(updated);
        return Task.CompletedTask;
    }

    public Task NextAsync()
    {
        NextCount++;
        return MoveTo(_index + 1);
    }

    public Task PreviousAsync()
    {
        PreviousCount++;
        return MoveTo(_index - 1);
    }

    public Task SeekAsync(TimeSpan position)
    {
        SeekCount++;
        MediaInfo? updated = null;
        lock (_gate)
        {
            if (_current is not null)
            {
                var clamped = position < TimeSpan.Zero ? TimeSpan.Zero
                    : position > _current.Duration ? _current.Duration
                    : position;
                _current = _current with { Position = clamped };
                updated = _current;
            }
        }

        if (updated is not null) Raise(updated);
        return Task.CompletedTask;
    }

    public void Dispose() { }

    private Task MoveTo(int index)
    {
        MediaInfo? updated = null;
        lock (_gate)
        {
            if (index >= 0 && index < _playlist.Count)
            {
                _index = index;
                _current = _playlist[index];
                updated = _current;
            }
        }

        // Out-of-range moves are no-ops and raise nothing.
        if (updated is not null) Raise(updated);
        return Task.CompletedTask;
    }

    // Called outside the lock, so handlers may call back into the fake.
    private void Raise(MediaInfo? media) => MediaChanged?.Invoke(this, media);
}

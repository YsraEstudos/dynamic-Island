using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Island.App.Widgets;
using Island.Core.Abstractions;
using Island.Core.Application;
using Island.Core.Configuration;
using Island.Core.Models;
using Island.Core.Pomodoro;

namespace Island.App.ViewModels;

/// <summary>
/// UI state of the island. Coordinator states arrive on arbitrary threads and are marshalled to the UI dispatcher.
/// Playback position is extrapolated from the last snapshot by a single timer, which runs only while the shelf
/// (which shows the now-playing widget) is open and media is playing. Pomodoro updates are coalesced by
/// <see cref="UiSignal"/>, so a burst of ticks costs one UI refresh.
/// </summary>
public sealed partial class IslandViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);
    private const int ArtworkDecodeWidth = 160;

    private readonly IslandCoordinator _coordinator;
    private readonly IMediaService _media;
    private readonly Func<IslandSettings> _settings;
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    private readonly DispatcherTimer _ticker;
    private readonly UiSignal _pomodoroSignal;

    private string? _trackKey;
    private byte[]? _lastArtworkBytes;
    private TimeSpan _snapshotPosition;
    private long _snapshotTimestamp;
    private bool _disposed;
    // The panel shown last (shelf or Clipboard), so a reopen from Compact returns to it.
    private IslandMode _lastPanel = IslandMode.Expanded;

    [ObservableProperty] private IslandMode _mode = IslandMode.Compact;
    [ObservableProperty] private bool _suspended;
    [ObservableProperty] private bool _hasMedia;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _artist = string.Empty;
    [ObservableProperty] private BitmapImage? _thumbnail;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private TimeSpan _position;
    [ObservableProperty] private TimeSpan _duration;
    [ObservableProperty] private int _volumeLevel;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private Notice? _notice;
    [ObservableProperty] private bool _pomodoroRunning;
    [ObservableProperty] private string _pomodoroText = string.Empty;
    [ObservableProperty] private PomodoroPhase _pomodoroPhase = PomodoroPhase.Focus;
    [ObservableProperty] private bool _pomodoroAngry;
    [ObservableProperty] private bool _pomodoroReduceMotion;

    public IslandViewModel(IslandCoordinator coordinator, IMediaService media, IVolumeService volume,
        Func<IslandSettings> settings, ShelfContext shelf)
    {
        _coordinator = coordinator;
        _media = media;
        _settings = settings;
        Shelf = shelf;
        _dispatcher = System.Windows.Application.Current?.Dispatcher
                      ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _ticker = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TickInterval };
        _ticker.Tick += (_, _) => Position = ComputePosition();
        _pomodoroSignal = new UiSignal(_dispatcher, RefreshPomodoro);

        VolumeLevel = volume.Current.Level;
        IsMuted = volume.Current.IsMuted;

        _coordinator.StateChanged += OnStateChanged;
        Shelf.Pomodoro.Changed += _pomodoroSignal.Signal;
        Shelf.Angry.LockChanged += _pomodoroSignal.Signal;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        RefreshPomodoro();
        Apply(_coordinator.State);
    }

    /// <summary>Services and shelf widget context (pomodoro, file tray, clipboard, media).</summary>
    public ShelfContext Shelf { get; }

    /// <summary>Current settings snapshot (read from the lead's settings source).</summary>
    public IslandSettings Settings => _settings();

    /// <summary>Progress 0..1 for the seek bar.</summary>
    public double Progress => Duration.TotalSeconds > 0
        ? Math.Clamp(Position.TotalSeconds / Duration.TotalSeconds, 0.0, 1.0)
        : 0.0;

    public string ElapsedText => FormatTime(Position);

    public string RemainingText
    {
        get
        {
            TimeSpan remaining = Duration - Position;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            // Round up so the countdown reaches 0:00 exactly when the track ends.
            remaining = TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds));
            return "-" + FormatTime(remaining);
        }
    }

    /// <summary>Volume as 0..1.</summary>
    public double VolumeFraction => VolumeLevel / 100.0;

    /// <summary>The equalizer glyph animates only when media is actually playing.</summary>
    public bool IsEqualizerActive => HasMedia && IsPlaying;

    partial void OnPositionChanged(TimeSpan value) => NotifyTimeChanged();

    partial void OnDurationChanged(TimeSpan value) => NotifyTimeChanged();

    partial void OnVolumeLevelChanged(int value) => OnPropertyChanged(nameof(VolumeFraction));

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(IsEqualizerActive));

    partial void OnHasMediaChanged(bool value) => OnPropertyChanged(nameof(IsEqualizerActive));

    [RelayCommand]
    private Task PlayPauseAsync() => _media.PlayPauseAsync();

    [RelayCommand]
    private Task NextAsync() => _media.NextAsync();

    [RelayCommand]
    private Task PreviousAsync() => _media.PreviousAsync();

    [RelayCommand]
    private void Expand() => _coordinator.Post(new IslandEvent.ExpandRequested());

    /// <summary>Reopens from Compact or a temporary state into the panel shown last (the Clipboard, or the shelf on its last row).</summary>
    [RelayCommand]
    private void Reopen()
    {
        bool clipboard = ReopenPolicy.Target(_lastPanel, Settings.ClipboardEnabled) == IslandMode.Clipboard;
        _coordinator.Post(clipboard ? new IslandEvent.ClipboardRequested() : new IslandEvent.ExpandRequested());
    }

    [RelayCommand]
    private void Collapse() => _coordinator.Post(new IslandEvent.CollapseRequested());

    /// <summary>Shrinks the island to the Mini pill (a fast throw upward, or the context menu).</summary>
    [RelayCommand]
    private void Minimize() => _coordinator.Post(new IslandEvent.MinimizeRequested());

    /// <summary>Context menu: enter shelf edit mode.</summary>
    [RelayCommand]
    private void OpenCustomize() => _coordinator.Post(new IslandEvent.CustomizeRequested());

    /// <summary>Context menu: open the clipboard panel.</summary>
    [RelayCommand]
    private void OpenClipboard() => _coordinator.Post(new IslandEvent.ClipboardRequested());

    /// <summary>Leaves edit mode without saving (back to the shelf).</summary>
    [RelayCommand]
    private void ExitCustomize() => _coordinator.Post(new IslandEvent.ExpandRequested());

    /// <summary>Saves the edited rows of widgets and returns to the shelf.</summary>
    public void CommitShelf(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        IslandSettings current = _settings();
        (IReadOnlyList<string> ids, IReadOnlyList<int> rowIndexes) = ShelfLayout.FromRows(rows);
        Shelf.ApplySettings(current with { ShelfWidgets = ids.ToArray(), ShelfRows = rowIndexes.ToArray() });
        _coordinator.Post(new IslandEvent.ExpandRequested());
    }

    /// <summary>Seeks to a fraction 0..1 of the track duration.</summary>
    [RelayCommand]
    private async Task SeekAsync(double fraction)
    {
        if (Duration <= TimeSpan.Zero) return;

        TimeSpan target = TimeSpan.FromTicks((long)(Duration.Ticks * Math.Clamp(fraction, 0.0, 1.0)));
        SetSnapshot(target);
        Position = target;
        await _media.SeekAsync(target);
    }

    /// <summary>Reports pointer hover or drag so the coordinator holds temporary states open.</summary>
    public void SetInteracting(bool interacting) =>
        _coordinator.Post(new IslandEvent.InteractionChanged(interacting));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _coordinator.StateChanged -= OnStateChanged;
        Shelf.Pomodoro.Changed -= _pomodoroSignal.Signal;
        Shelf.Angry.LockChanged -= _pomodoroSignal.Signal;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _pomodoroSignal.Dispose();
        _ticker.Stop();
    }

    private void OnStateChanged(IslandState state)
    {
        if (_disposed) return;
        _dispatcher.InvokeAsync(() =>
        {
            if (!_disposed) Apply(state);
        });
    }

    private void Apply(IslandState state)
    {
        Suspended = state.Suspended;
        Mode = state.Mode;
        if (state.Mode == IslandMode.Clipboard) _lastPanel = IslandMode.Clipboard;
        else if (state.Mode is IslandMode.Expanded or IslandMode.Customize) _lastPanel = IslandMode.Expanded;
        Notice = state.Notice;
        if (state.Volume is { } volume)
        {
            VolumeLevel = volume.Level;
            IsMuted = volume.IsMuted;
        }
        ApplyMedia(state.Media);
        UpdateTicker();
    }

    /// <summary>Reads the pomodoro timer on the UI thread. Property setters raise only when the value changes.</summary>
    private void RefreshPomodoro()
    {
        if (_disposed) return;
        var timer = Shelf.Pomodoro;
        PomodoroRunning = timer.IsRunning;
        PomodoroText = FormatCountdown(timer.Remaining);
        PomodoroPhase = timer.Phase;
        PomodoroAngry = Shelf.Angry.IsLocked;
        PomodoroReduceMotion = Settings.ReduceAnimations || !SystemParameters.ClientAreaAnimation;
    }

    /// <summary>Refreshes compact motion preferences immediately after settings are applied.</summary>
    public void RefreshMotionPreferences() => _pomodoroSignal.Signal();

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e) => _pomodoroSignal.Signal();

    private void ApplyMedia(MediaInfo? media)
    {
        HasMedia = media is not null;

        if (media is null)
        {
            _trackKey = null;
            _lastArtworkBytes = null;
            Title = string.Empty;
            Artist = string.Empty;
            Thumbnail = null;
            IsPlaying = false;
            Duration = TimeSpan.Zero;
            SetSnapshot(TimeSpan.Zero);
            Position = TimeSpan.Zero;
            return;
        }

        string key = media.TrackKey;
        if (key != _trackKey)
        {
            // Artwork is decoded once per track, not on every position update.
            _trackKey = key;
            Title = media.Title;
            Artist = media.Artist;
            _lastArtworkBytes = media.Thumbnail;
            Thumbnail = DecodeArtwork(media.Thumbnail);
        }
        else if (Thumbnail is null && media.Thumbnail is not null
                 && !ReferenceEquals(media.Thumbnail, _lastArtworkBytes))
        {
            _lastArtworkBytes = media.Thumbnail;
            Thumbnail = DecodeArtwork(media.Thumbnail);
        }

        IsPlaying = media.IsPlaying;
        Duration = media.Duration;
        SetSnapshot(media.Position);
        Position = ComputePosition();
    }

    /// <summary>The ticker runs only while the shelf (now-playing widget) is shown and media plays.</summary>
    private void UpdateTicker()
    {
        bool shelfShown = Mode is IslandMode.Expanded or IslandMode.Customize;
        bool wanted = !_disposed && shelfShown && IsPlaying;
        if (wanted && !_ticker.IsEnabled)
        {
            Position = ComputePosition();
            _ticker.Start();
        }
        else if (!wanted && _ticker.IsEnabled)
        {
            _ticker.Stop();
        }
    }

    private void SetSnapshot(TimeSpan position)
    {
        _snapshotPosition = position;
        _snapshotTimestamp = Stopwatch.GetTimestamp();
    }

    private TimeSpan ComputePosition()
    {
        TimeSpan position = _snapshotPosition;
        if (IsPlaying) position += Stopwatch.GetElapsedTime(_snapshotTimestamp);

        if (Duration > TimeSpan.Zero && position > Duration) position = Duration;
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        return position;
    }

    private void NotifyTimeChanged()
    {
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(RemainingText));
    }

    private static BitmapImage? DecodeArtwork(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;

        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = ArtworkDecodeWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // Undecodable artwork falls back to the placeholder.
            return null;
        }
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    /// <summary>Countdown text: rounds up so it reaches zero exactly when the phase ends.</summary>
    private static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return FormatTime(TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds)));
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.App.Animations;
using Island.App.Widgets;
using Island.Core.Abstractions;
using Island.Core.Audio;
using Island.Windows.Audio;
using UserControl = System.Windows.Controls.UserControl;

namespace Island.App.Views.Widgets;

/// <summary>
/// Shelf card with the volume of each app that has audio on the default output: three rows at a time, the foreground app
/// first, then the apps playing now, then the silent ones (faded). It reacts to the mixer's change events and reads peaks
/// only while it is visible and something plays, so a hidden mixer costs nothing.
/// </summary>
public partial class MixerWidget : UserControl, IFrameClient
{
    private const string DiscordProcess = "Discord";
    private const double ScrollSettleSeconds = 0.22;

    private readonly IAudioMixerService _mixer;
    private readonly ShelfContext _context;
    private readonly UiSignal _signal;
    private readonly Dictionary<string, MixerRow> _rows = new(StringComparer.Ordinal);
    private readonly SpringValue _scroll = new(SpringValue.OmegaForSettleTime(ScrollSettleSeconds, 1.0), 1.0, 0.0);
    private readonly Geometry _speaker;
    private readonly Geometry _speakerMuted;
    private IReadOnlyList<AppAudioSession> _ordered = Array.Empty<AppAudioSession>();
    private IDisposable? _metering;
    private bool _subscribed;
    private int _offset;

    public MixerWidget(ShelfContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        InitializeComponent();
        _context = context;
        _mixer = context.Mixer ?? throw new InvalidOperationException("Audio mixer is not available.");
        _signal = new UiSignal(Dispatcher, Refresh);
        _speaker = (Geometry)FindResource("WidgetIcon.Speaker");
        _speakerMuted = (Geometry)FindResource("WidgetIcon.SpeakerMuted");

        PrevButton.Click += () => Scroll(-1);
        NextButton.Click += () => Scroll(1);
        DiscordButton.Click += ToggleDiscord;
        Viewport.MouseWheel += (_, e) =>
        {
            Scroll(e.Delta < 0 ? 1 : -1);
            e.Handled = true;
        };
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
        IsVisibleChanged += (_, _) => SyncMetering();
    }

    public bool OnFrame(double dt)
    {
        bool settled = _scroll.Advance(dt);
        ApplyScroll();
        return !settled;
    }

    private void Subscribe()
    {
        if (_subscribed) return;

        _subscribed = true;
        _mixer.Changed += OnMixerChanged;
        _mixer.Start();
        Refresh();
        SyncMetering();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        _subscribed = false;
        _mixer.Changed -= OnMixerChanged;
        ReleaseMetering();
    }

    private void OnMixerChanged(object? sender, EventArgs e) => _signal.Signal();

    /// <summary>Peaks are read only while the card is on screen; the scope is released as soon as it is not.</summary>
    private void SyncMetering()
    {
        bool wanted = _subscribed && IsLoaded && IsVisible;
        if (wanted && _metering is null) _metering = _mixer.BeginMetering();
        else if (!wanted) ReleaseMetering();
    }

    private void ReleaseMetering()
    {
        _metering?.Dispose();
        _metering = null;
    }

    private void Refresh()
    {
        _ordered = AudioSessionOrdering.Order(_mixer.Sessions, ForegroundProcess.CurrentId());

        int clamped = AudioListPaging.Clamp(_offset, _ordered.Count);
        bool offsetChanged = clamped != _offset;
        _offset = clamped;

        SyncRows();
        if (offsetChanged) MoveToOffset(animated: false);
        UpdateFooter();
        UpdateDiscord();
    }

    /// <summary>Makes the rows match the ordered sessions: creates new rows, removes gone ones, and reorders the panel when needed.</summary>
    private void SyncRows()
    {
        var present = new HashSet<string>(_ordered.Select(s => s.Id), StringComparer.Ordinal);
        foreach (string gone in _rows.Keys.Where(id => !present.Contains(id)).ToList())
        {
            RowsPanel.Children.Remove(_rows[gone].Root);
            _rows.Remove(gone);
        }

        foreach (AppAudioSession session in _ordered)
        {
            if (!_rows.TryGetValue(session.Id, out MixerRow? row))
            {
                row = new MixerRow(session.Id, _speaker, _speakerMuted, _mixer.SetVolume, _mixer.SetMuted);
                _rows.Add(session.Id, row);
            }
            row.Update(session);
        }

        if (!PanelMatchesOrder())
        {
            RowsPanel.Children.Clear();
            foreach (AppAudioSession session in _ordered) RowsPanel.Children.Add(_rows[session.Id].Root);
        }

        RowsPanel.Height = _ordered.Count * MixerRow.Height;
        ApplyScroll();

        bool any = _ordered.Count > 0;
        RowsPanel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        CountText.Text = any ? _ordered.Count.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private bool PanelMatchesOrder()
    {
        if (RowsPanel.Children.Count != _ordered.Count) return false;

        for (int i = 0; i < _ordered.Count; i++)
        {
            if (!ReferenceEquals(RowsPanel.Children[i], _rows[_ordered[i].Id].Root)) return false;
        }
        return true;
    }

    private void Scroll(int delta)
    {
        int next = AudioListPaging.Step(_offset, delta, _ordered.Count);
        if (next == _offset) return;

        _offset = next;
        MoveToOffset(animated: true);
        UpdateFooter();
    }

    /// <summary>The list glides to the row offset with a critically damped spring; reduced motion jumps.</summary>
    private void MoveToOffset(bool animated)
    {
        if (animated && !_context.Settings().ReduceAnimations)
        {
            _scroll.SetTarget(_offset);
            MotionPump.Request(this);
            return;
        }

        _scroll.Snap(_offset);
        ApplyScroll();
    }

    /// <summary>
    /// Places each row on the canvas, shifted by the current scroll position. The rows are moved directly, not through a
    /// transform on the panel: a transform on the panel moved the viewport's clip with it and cut the rows off.
    /// </summary>
    private void ApplyScroll()
    {
        double shift = _scroll.Current * MixerRow.Height;
        for (int i = 0; i < _ordered.Count; i++)
        {
            if (_rows.TryGetValue(_ordered[i].Id, out MixerRow? row)) Canvas.SetTop(row.Root, i * MixerRow.Height - shift);
        }
    }

    private void UpdateFooter()
    {
        int count = _ordered.Count;
        bool paged = count > AudioListPaging.RowsPerPage;
        Visibility arrows = paged ? Visibility.Visible : Visibility.Collapsed;
        PrevButton.Visibility = arrows;
        NextButton.Visibility = arrows;
        PrevButton.IsEnabled = _offset > 0;
        NextButton.IsEnabled = _offset < AudioListPaging.MaxOffset(count);
        PageText.Text = AudioListPaging.Label(_offset, count);
    }

    /// <summary>The Discord shortcut only appears while Discord has a session; its label says what the click will do.</summary>
    private void UpdateDiscord()
    {
        IReadOnlyList<AppAudioSession> matches = AudioSessionMatcher.FindByProcessName(_ordered, DiscordProcess);
        DiscordButton.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DiscordButton.LabelText = matches.Count > 0 && matches.All(s => s.Muted) ? "Ligar Discord" : "Mutar Discord";
    }

    private void ToggleDiscord()
    {
        IReadOnlyList<AppAudioSession> matches = AudioSessionMatcher.FindByProcessName(_mixer.Sessions, DiscordProcess);
        if (matches.Count == 0) return;

        bool mute = !matches.All(s => s.Muted);
        foreach (AppAudioSession session in matches) _mixer.SetMuted(session.Id, mute);
    }
}

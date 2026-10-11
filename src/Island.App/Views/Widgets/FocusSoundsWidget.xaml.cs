using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.App.Widgets;
using Island.Core.Tools.FocusSounds;
using Island.Windows.Audio;
using UserControl = System.Windows.Controls.UserControl;

namespace Island.App.Views.Widgets;

/// <summary>
/// Sons de foco (340 x 152): eight generated ambient sounds that play together, each with its own level. The chips switch
/// sounds on and off, the wheel over a chip sets its level, play/pause fades the mix in and out, and the sleep button
/// cycles a timer that fades out over its last ten seconds. The model lives in the tool registry, so the sounds keep
/// playing when the shelf is rebuilt. The equalizer moves only while sound plays and the widget is on screen.
/// </summary>
public partial class FocusSoundsWidget : UserControl
{
    private const double WheelLevelStep = 0.05;

    private readonly ShelfContext _context;
    private readonly FocusSoundsModel _model;
    private readonly UiSignal _signal;
    private readonly Dictionary<FocusSoundKind, FocusSoundChip> _chips = new();
    private string _shownSleep = string.Empty;
    private bool _subscribed;

    public FocusSoundsWidget(ShelfContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        InitializeComponent();
        _context = context;
        _model = context.Tools.GetOrCreate(() => new FocusSoundsModel(
            context.FocusAudioOutput(), TimeProvider.System, (uint)Random.Shared.Next(1, int.MaxValue)));
        _signal = new UiSignal(Dispatcher, Refresh);

        BuildChips();
        PlayButton.Click += () => _model.TogglePlay();
        SleepButton.Click += () => _model.CycleSleepTimer();
        VolumeBar.Dragged += value => _model.SetVolume((float)value);

        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
        IsVisibleChanged += (_, _) => SyncEqualizer();
    }

    private void Subscribe()
    {
        if (_subscribed) return;

        _subscribed = true;
        _model.Changed += OnModelChanged;
        Refresh();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        _subscribed = false;
        _model.Changed -= OnModelChanged;
        Equalizer.SetAnimating(false);
    }

    /// <summary>The model raises its changes from timer threads; the signal brings them back to the UI.</summary>
    private void OnModelChanged(object? sender, EventArgs e) => _signal.Signal();

    private void BuildChips()
    {
        foreach (FocusSoundKind kind in Enum.GetValues<FocusSoundKind>())
        {
            var chip = new FocusSoundChip(kind, FocusSoundsFormat.SoundName(kind), (Geometry)FindResource(GlyphKey(kind)))
            {
                Margin = new Thickness(3),
            };
            chip.Clicked += () => _model.SetOn(kind, !_model.IsOn(kind));
            chip.Wheeled += step => _model.SetLevel(kind, _model.Level(kind) + (float)(step * WheelLevelStep));

            _chips.Add(kind, chip);
            ChipGrid.Children.Add(chip);
        }
    }

    private void Refresh()
    {
        bool playing = _model.IsPlaying;
        bool reduce = _context.Settings().ReduceAnimations;

        PlayButton.IsAltShown = playing;
        PlayButton.IsEnabled = playing || _model.CanPlay;

        string sleep = FocusSoundsFormat.SleepLabel(_model.SleepMinutes, _model.SleepRemaining);
        if (sleep != _shownSleep)
        {
            _shownSleep = sleep;
            SleepButton.LabelText = sleep;
        }

        foreach ((FocusSoundKind kind, FocusSoundChip chip) in _chips)
        {
            chip.Render(_model.IsOn(kind), _model.Level(kind), reduce);
        }

        VolumeBar.Value = _model.Volume;
        VolumeText.Text = FocusSoundsFormat.Percent(_model.Volume);
        ShowStatus();
        SyncEqualizer();
    }

    private void ShowStatus()
    {
        string status = !_model.IsAudioAvailable ? FocusSoundsFormat.NoAudioOutput
            : !_model.CanPlay ? FocusSoundsFormat.PickASound
            : string.Empty;

        StatusText.Text = status;
        StatusText.Visibility = status.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SyncEqualizer()
    {
        bool animate = _model.IsPlaying && IsVisible && !_context.Settings().ReduceAnimations;
        Equalizer.SetAnimating(animate);
    }

    private static string GlyphKey(FocusSoundKind kind) => kind switch
    {
        FocusSoundKind.Rain => "FocusGlyph.Rain",
        FocusSoundKind.WhiteNoise => "FocusGlyph.WhiteNoise",
        FocusSoundKind.PinkNoise => "FocusGlyph.PinkNoise",
        FocusSoundKind.BrownNoise => "FocusGlyph.BrownNoise",
        FocusSoundKind.Waves => "FocusGlyph.Waves",
        FocusSoundKind.Fire => "FocusGlyph.Fire",
        FocusSoundKind.Wind => "FocusGlyph.Wind",
        FocusSoundKind.Cafe => "FocusGlyph.Cafe",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown focus sound."),
    };
}

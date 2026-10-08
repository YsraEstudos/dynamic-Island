using System.Windows.Media;
using Island.App.Shell;
using Island.App.Widgets;
using Island.Core.Configuration;
using Island.Core.Pomodoro;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using UserControl = System.Windows.Controls.UserControl;

namespace Island.App.Views.Widgets;

/// <summary>
/// Pomodoro widget (300 x 152). Phase pills (Focus, Break and Angry), a minute ruler (editable only while stopped),
/// play/pause, sound toggle, reset and the remaining time. The Angry pill starts a locked focus session: while locked the
/// other controls dim, the timer ignores changes, and clicking Angry opens the unlock dialog instead.
/// Everything reads the core timer; the widget adds no timer of its own. Timer changes arrive on arbitrary threads and
/// are coalesced onto the UI thread, where the text is updated only when it changed.
/// </summary>
public partial class PomodoroWidget : UserControl
{
    private static readonly Brush Orange = Frozen(0xFF, 0x9F, 0x0A, 0xFF);
    private static readonly Brush OrangeSoft = Frozen(0xFF, 0x9F, 0x0A, 0x33);
    private static readonly Brush Red = Frozen(0xFF, 0x45, 0x3A, 0xFF);
    private static readonly Brush RedSoft = Frozen(0xFF, 0x45, 0x3A, 0x33);
    private static readonly Brush Chip = Frozen(0x2C, 0x2C, 0x2E, 0xFF);
    private static readonly Brush Grey = Frozen(0xA1, 0xA1, 0xA6, 0xFF);

    private readonly ShelfContext _ctx;
    private readonly UiSignal _signal;
    private string _shownTime = string.Empty;
    private bool _subscribed;
    private bool? _shownLocked;

    public PomodoroWidget(ShelfContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        _signal = new UiSignal(Dispatcher, Refresh);

        FocusPill.LabelText = "Focus";
        BreakPill.LabelText = "Break";
        FocusPill.LabelBrush = Grey;
        BreakPill.LabelBrush = Grey;
        FocusPill.Background = Chip;
        BreakPill.Background = Chip;
        FocusPill.Click += () => _ctx.Pomodoro.SetPhase(PomodoroPhase.Focus);
        BreakPill.Click += () => _ctx.Pomodoro.SetPhase(PomodoroPhase.Break);

        AngryPill.LabelText = "Angry";
        AngryPill.LabelBrush = Grey;
        AngryPill.Background = Chip;
        AngryPill.Click += OnAngryClicked;

        PlayButton.Click += () => _ctx.Pomodoro.Toggle();
        ResetButton.Click += () => _ctx.Pomodoro.Reset();
        SoundButton.Click += ToggleSound;

        // Live drag: the timer's duration follows the ruler; release stores it in the settings for the phase.
        Ruler.ValueDragged += minutes => _ctx.Pomodoro.SetMinutes(minutes);
        Ruler.ValueCommitted += PersistMinutes;

        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribed) return;

        _ctx.Pomodoro.Changed += OnPomodoroChanged;
        _ctx.Angry.LockChanged += OnLockChanged;
        _subscribed = true;
        Refresh();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        _ctx.Pomodoro.Changed -= OnPomodoroChanged;
        _ctx.Angry.LockChanged -= OnLockChanged;
        _subscribed = false;
    }

    /// <summary>Arbitrary thread.</summary>
    private void OnPomodoroChanged() => _signal.Signal();

    /// <summary>Arbitrary thread.</summary>
    private void OnLockChanged() => _signal.Signal();

    private void Refresh()
    {
        PomodoroTimer timer = _ctx.Pomodoro;
        bool running = timer.IsRunning;
        bool locked = _ctx.Angry.IsLocked;
        PomodoroPhase phase = timer.Phase;

        string time = FormatRemaining(timer.Remaining);
        if (time != _shownTime)
        {
            _shownTime = time;
            TimeText.Text = time;
        }

        if (locked != _shownLocked)
        {
            _shownLocked = locked;
            TimeText.Foreground = locked ? Red : Orange;
        }

        ApplyPill(FocusPill, phase == PomodoroPhase.Focus && !locked);
        ApplyPill(BreakPill, phase == PomodoroPhase.Break && !locked);
        ApplyAngryPill(locked);

        // Locked: the controls are dimmed. The timer itself ignores pause, reset, phase and duration changes.
        double dim = locked ? 0.4 : 1.0;
        FocusPill.Opacity = dim;
        BreakPill.Opacity = dim;
        PlayButton.Opacity = dim;
        ResetButton.Opacity = dim;

        PlayButton.IsAltShown = running;
        SoundButton.IsAltShown = !_ctx.Settings().PomodoroSound;

        int minutes = (int)Math.Round(timer.PhaseDuration.TotalMinutes);
        Ruler.Value = Math.Clamp(minutes, MinuteRuler.MinMinutes, MinuteRuler.MaxMinutes);
        Ruler.Editable = !running && !locked;
        Ruler.Opacity = running || locked ? 0.4 : 1.0;
    }

    /// <summary>Locked: opens the unlock dialog. Otherwise starts a locked focus session.</summary>
    private void OnAngryClicked()
    {
        if (_ctx.Angry.IsLocked) UnlockWindow.ShowFor(_ctx.Angry);
        else _ctx.Angry.Engage();
    }

    private void ToggleSound()
    {
        IslandSettings settings = _ctx.Settings();
        _ctx.ApplySettings(settings with { PomodoroSound = !settings.PomodoroSound });
        Refresh();
    }

    private void PersistMinutes(int minutes)
    {
        IslandSettings settings = _ctx.Settings();
        IslandSettings updated = _ctx.Pomodoro.Phase == PomodoroPhase.Focus
            ? settings with { PomodoroFocusMinutes = minutes }
            : settings with { PomodoroBreakMinutes = minutes };
        _ctx.ApplySettings(updated);
    }

    private static void ApplyPill(Island.App.Widgets.ShelfButton pill, bool selected)
    {
        pill.Background = selected ? OrangeSoft : Chip;
        pill.LabelBrush = selected ? Orange : Grey;
    }

    private void ApplyAngryPill(bool selected)
    {
        AngryPill.Background = selected ? RedSoft : Chip;
        AngryPill.LabelBrush = selected ? Red : Grey;
    }

    /// <summary>mm:ss, rounded up so the display reads 00:00 only when the phase has ended.</summary>
    private static string FormatRemaining(TimeSpan remaining)
    {
        long seconds = (long)Math.Ceiling(Math.Max(0.0, remaining.TotalSeconds));
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private static Brush Frozen(byte r, byte g, byte b, byte a)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

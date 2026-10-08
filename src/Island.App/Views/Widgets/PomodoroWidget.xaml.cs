using System.Windows.Input;
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
/// Pomodoro widget (300 x 152). Phase pills (Focus, Break and Angry), a count pill (xN while stopped, N/M progress while
/// a plan runs), a minute ruler (editable only while stopped and without a plan), play/pause, sound toggle, reset and the
/// remaining time. The Angry pill starts a locked focus session: while locked the other controls dim, the timer ignores
/// changes, and clicking Angry opens the unlock dialog instead. A plan (Play with N pomodoros) owns the phase, so the
/// Focus and Break pills and the ruler dim until it ends or is reset.
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
    private string _shownCycles = string.Empty;
    private bool? _shownPlanActive;

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
        FocusPill.Click += () => SelectPhase(PomodoroPhase.Focus);
        BreakPill.Click += () => SelectPhase(PomodoroPhase.Break);

        AngryPill.LabelText = "Angry";
        AngryPill.LabelBrush = Grey;
        AngryPill.Background = Chip;
        AngryPill.Click += OnAngryClicked;

        CyclesPill.Background = Chip;
        CyclesPill.LabelBrush = Grey;
        CyclesPill.Click += CycleCount;
        CyclesPill.MouseWheel += OnCyclesWheel;

        PlayButton.Click += () => _ctx.Pomodoro.Play(_ctx.Settings().PomodoroCycles);
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
        IslandSettings settings = _ctx.Settings();
        bool running = timer.IsRunning;
        bool locked = _ctx.Angry.IsLocked;
        bool planActive = timer.PlanActive;
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

        string cycles = planActive ? $"{timer.Cycle}/{timer.TotalCycles}" : $"×{settings.PomodoroCycles}";
        if (cycles != _shownCycles)
        {
            _shownCycles = cycles;
            CyclesPill.LabelText = cycles;
        }

        if (planActive != _shownPlanActive)
        {
            _shownPlanActive = planActive;
            CyclesPill.Background = planActive ? OrangeSoft : Chip;
            CyclesPill.LabelBrush = planActive ? Orange : Grey;
        }

        ApplyPill(FocusPill, phase == PomodoroPhase.Focus && !locked);
        ApplyPill(BreakPill, phase == PomodoroPhase.Break && !locked);
        ApplyAngryPill(locked);

        // Locked: the controls are dimmed. The timer itself ignores pause, reset, phase and duration changes.
        // A plan owns the phase, so the phase pills dim as well.
        bool phaseLocked = locked || planActive;
        FocusPill.Opacity = phaseLocked ? 0.4 : 1.0;
        BreakPill.Opacity = phaseLocked ? 0.4 : 1.0;
        // The count can only change while stopped and unlocked; during a plan it shows progress at full strength.
        CyclesPill.Opacity = planActive || (!running && !locked) ? 1.0 : 0.4;
        double dim = locked ? 0.4 : 1.0;
        PlayButton.Opacity = dim;
        ResetButton.Opacity = dim;

        PlayButton.IsAltShown = running;
        SoundButton.IsAltShown = !settings.PomodoroSound;

        int minutes = (int)Math.Round(timer.PhaseDuration.TotalMinutes);
        Ruler.Value = Math.Clamp(minutes, MinuteRuler.MinMinutes, MinuteRuler.MaxMinutes);
        Ruler.Editable = !running && !locked && !planActive;
        Ruler.Opacity = running || locked || planActive ? 0.4 : 1.0;
    }

    /// <summary>Phase pills are ignored during a plan: the plan decides the phase.</summary>
    private void SelectPhase(PomodoroPhase phase)
    {
        if (!_ctx.Pomodoro.PlanActive) _ctx.Pomodoro.SetPhase(phase);
    }

    /// <summary>Locked: opens the unlock dialog. Otherwise starts a locked focus session.</summary>
    private void OnAngryClicked()
    {
        if (_ctx.Angry.IsLocked) UnlockWindow.ShowFor(_ctx.Angry);
        else _ctx.Angry.Engage();
    }

    /// <summary>Click: +1, wrapping from the maximum back to the minimum.</summary>
    private void CycleCount()
    {
        if (!CanEditCycles()) return;

        int current = _ctx.Settings().PomodoroCycles;
        SetCycles(current >= PomodoroTimer.MaxCycles ? PomodoroTimer.MinCycles : current + 1);
    }

    /// <summary>Wheel up +1, wheel down -1, limited to the range. Always handled so the wheel does not reach the island.</summary>
    private void OnCyclesWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (!CanEditCycles()) return;

        int step = e.Delta > 0 ? 1 : -1;
        SetCycles(_ctx.Settings().PomodoroCycles + step);
    }

    private bool CanEditCycles()
    {
        PomodoroTimer timer = _ctx.Pomodoro;
        return !timer.IsRunning && !timer.PlanActive && !_ctx.Angry.IsLocked;
    }

    private void SetCycles(int cycles)
    {
        IslandSettings settings = _ctx.Settings();
        int clamped = Math.Clamp(cycles, PomodoroTimer.MinCycles, PomodoroTimer.MaxCycles);
        if (clamped == settings.PomodoroCycles) return;

        _ctx.ApplySettings(settings with { PomodoroCycles = clamped });
        Refresh();
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

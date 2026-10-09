using System.Globalization;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Island.App.Shell;
using Island.App.Widgets;
using Island.Core.Configuration;
using Island.Core.Pomodoro;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using UserControl = System.Windows.Controls.UserControl;

namespace Island.App.Views.Widgets;

/// <summary>
/// Pomodoro widget (300 x 152). Phase pills (Pré, Focus, Break and Angry) on the left and a stepper on the right
/// (− N + pomodoros, 1 to 24, usable while stopped, running or locked; a running plan changes its total live). Below
/// them a plan track (one segment per pomodoro, filled as the plan advances) and a forecast of when the plan ends
/// ("até 15:30 · 1h30"). Then a minute ruler (editable only while stopped, without a plan and outside Pré), play/pause,
/// sound toggle, reset and the remaining time. Pré is a fixed 5-minute preparation before studying; it is started alone,
/// never as part of a plan. The Angry pill selects a locked focus plan of the chosen pomodoros. Play starts it: each
/// focus is locked and each break is free. While locked the other controls dim, the timer ignores changes, and clicking
/// Angry opens the unlock dialog instead. A plan (Play with N pomodoros) owns the phase, so the Focus and Break pills and the ruler dim
/// until it ends or is reset. The forecast clock refreshes every 15 s while the timer is stopped.
/// The clock button schedules a start (a clock time, Focus or Angry, and a count) through <see cref="ScheduleWindow"/>;
/// while a start is pending and nothing runs, the forecast shows its time instead ("às 14:30 · Focus ×4"). The clock
/// button dims while locked and then ignores clicks.
/// Everything reads the core timer; the widget adds only the forecast ticker. Timer changes arrive on arbitrary threads
/// and are coalesced onto the UI thread, where the text is updated only when it changed.
/// </summary>
public partial class PomodoroWidget : UserControl
{
    private static readonly Brush Orange = Frozen(0xFF, 0x9F, 0x0A, 0xFF);
    private static readonly Brush OrangeSoft = Frozen(0xFF, 0x9F, 0x0A, 0x33);
    private static readonly Brush Red = Frozen(0xFF, 0x45, 0x3A, 0xFF);
    private static readonly Brush RedSoft = Frozen(0xFF, 0x45, 0x3A, 0x33);
    private static readonly Brush Chip = Frozen(0x2C, 0x2C, 0x2E, 0xFF);
    private static readonly Brush Grey = Frozen(0xA1, 0xA1, 0xA6, 0xFF);
    private static readonly Brush White = Frozen(0xF2, 0xF2, 0xF7, 0xFF);
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TimeSpan ForecastInterval = TimeSpan.FromSeconds(15);

    private readonly ShelfContext _ctx;
    private readonly UiSignal _signal;
    private string _shownTime = string.Empty;
    private bool _subscribed;
    private bool _angrySelected;
    private bool? _shownLocked;
    private int _shownCycles = -1;
    private string _shownForecast = string.Empty;
    private Brush? _shownScheduleBrush;
    private DispatcherTimer? _forecastTimer;

    public PomodoroWidget(ShelfContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        _signal = new UiSignal(Dispatcher, Refresh);

        PrepPill.LabelText = "Pré";
        FocusPill.LabelText = "Focus";
        BreakPill.LabelText = "Break";
        PrepPill.LabelBrush = Grey;
        FocusPill.LabelBrush = Grey;
        BreakPill.LabelBrush = Grey;
        PrepPill.Background = Chip;
        FocusPill.Background = Chip;
        BreakPill.Background = Chip;
        PrepPill.Click += () => SelectPhase(PomodoroPhase.Prep);
        FocusPill.Click += () => SelectPhase(PomodoroPhase.Focus);
        BreakPill.Click += () => SelectPhase(PomodoroPhase.Break);

        AngryPill.LabelText = "Angry";
        AngryPill.LabelBrush = Grey;
        AngryPill.Background = Chip;
        AngryPill.Click += OnAngryClicked;

        Stepper.Stepped += OnStepped;

        PlayButton.Click += OnPlayClicked;
        ResetButton.Click += () => _ctx.Pomodoro.Reset();
        ScheduleButton.Click += OnScheduleClicked;
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
        _ctx.Schedule.Changed += OnScheduleChanged;
        _subscribed = true;
        _forecastTimer ??= CreateForecastTimer();
        Refresh();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        _ctx.Pomodoro.Changed -= OnPomodoroChanged;
        _ctx.Angry.LockChanged -= OnLockChanged;
        _ctx.Schedule.Changed -= OnScheduleChanged;
        _subscribed = false;
        _forecastTimer?.Stop();
    }

    /// <summary>Arbitrary thread.</summary>
    private void OnPomodoroChanged() => _signal.Signal();

    /// <summary>Arbitrary thread.</summary>
    private void OnLockChanged() => _signal.Signal();

    /// <summary>Arbitrary thread.</summary>
    private void OnScheduleChanged() => _signal.Signal();

    /// <summary>Ticks the forecast clock while the timer is stopped; a running timer already refreshes on every change.</summary>
    private DispatcherTimer CreateForecastTimer()
    {
        var timer = new DispatcherTimer { Interval = ForecastInterval };
        timer.Tick += (_, _) => Refresh();
        return timer;
    }

    private void Refresh()
    {
        PomodoroTimer timer = _ctx.Pomodoro;
        IslandSettings settings = _ctx.Settings();
        bool running = timer.IsRunning;
        bool locked = _ctx.Angry.IsLocked;
        bool sessionActive = _ctx.Angry.IsSessionActive;
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

        // The stepper and the track share the count: the plan's total while it runs, otherwise the setting.
        bool reduce = settings.ReduceAnimations;
        int cycles = planActive ? timer.TotalCycles : settings.PomodoroCycles;
        Stepper.Accent = planActive;
        Stepper.ReduceMotion = reduce;
        Stepper.CanDecrease = planActive ? timer.TotalCycles > timer.Cycle : cycles > PomodoroTimer.MinCycles;
        Stepper.CanIncrease = cycles < PomodoroTimer.MaxCycles;
        Track.Accent = planActive;
        Track.Angry = sessionActive;
        Track.ReduceMotion = reduce;
        if (cycles != _shownCycles)
        {
            _shownCycles = cycles;
            Stepper.Value = cycles;
            Track.Total = cycles;
        }
        Track.Progress = planActive ? PlanProgress(timer, settings) : 0.0;

        ScheduledStart? pending = _ctx.Schedule.Pending;
        RefreshForecast(timer, settings, planActive, pending);

        ApplyPill(PrepPill, phase == PomodoroPhase.Prep && !locked);
        ApplyPill(FocusPill, phase == PomodoroPhase.Focus && !locked);
        ApplyPill(BreakPill, phase == PomodoroPhase.Break && !locked);
        ApplyAngryPill(locked, sessionActive, _angrySelected);

        // Locked: the controls are dimmed. The timer itself ignores pause, reset, phase and duration changes.
        // A plan owns the phase, so the phase pills dim as well. The stepper stays enabled: the count can change while locked.
        bool phaseLocked = locked || planActive;
        PrepPill.Opacity = phaseLocked ? 0.4 : 1.0;
        FocusPill.Opacity = phaseLocked ? 0.4 : 1.0;
        BreakPill.Opacity = phaseLocked ? 0.4 : 1.0;
        double dim = locked ? 0.4 : 1.0;
        PlayButton.Opacity = dim;
        ResetButton.Opacity = dim;
        ScheduleButton.Opacity = dim;

        // A pending scheduled start colours the clock icon: orange for Focus, red for Angry.
        Brush scheduleBrush = pending is null ? Grey
            : pending.Mode == ScheduledStartMode.Angry ? Red : Orange;
        if (!ReferenceEquals(scheduleBrush, _shownScheduleBrush))
        {
            _shownScheduleBrush = scheduleBrush;
            ScheduleButton.IconBrush = scheduleBrush;
        }

        PlayButton.IsAltShown = running;
        SoundButton.IsAltShown = !settings.PomodoroSound;

        int minutes = (int)Math.Round(timer.PhaseDuration.TotalMinutes);
        Ruler.Value = Math.Clamp(minutes, MinuteRuler.MinMinutes, MinuteRuler.MaxMinutes);
        // Pré has its fixed length, so the ruler must not write to the break setting through PersistMinutes.
        bool rulerFixed = running || locked || planActive || phase == PomodoroPhase.Prep;
        Ruler.Editable = !rulerFixed;
        Ruler.Opacity = rulerFixed ? 0.4 : 1.0;

        // The forecast clock only needs ticking while the timer is stopped; a running timer refreshes on every change.
        if (_forecastTimer is not null)
        {
            bool wantTicks = _subscribed && !running;
            if (wantTicks && !_forecastTimer.IsEnabled) _forecastTimer.Start();
            else if (!wantTicks && _forecastTimer.IsEnabled) _forecastTimer.Stop();
        }
    }

    /// <summary>Completed pomodoros plus the fraction of the current one (a pomodoro is focus plus break).</summary>
    private static double PlanProgress(PomodoroTimer timer, IslandSettings settings)
    {
        double cycle = Math.Max(1, settings.PomodoroFocusMinutes) + Math.Max(1, settings.PomodoroBreakMinutes);
        double focus = Math.Max(1, settings.PomodoroFocusMinutes);
        double elapsed = Math.Clamp((timer.PhaseDuration - timer.Remaining).TotalMinutes, 0.0, timer.PhaseDuration.TotalMinutes);

        double within = timer.Phase switch
        {
            PomodoroPhase.Focus => elapsed,
            PomodoroPhase.Break => focus + elapsed,
            _ => 0.0,
        };
        return Math.Clamp((timer.Cycle - 1) + within / cycle, 0.0, timer.TotalCycles);
    }

    /// <summary>
    /// "até 15:30 · 1h30": the clock time the remaining work ends and its length. Rebuilt only when the text changes.
    /// Shows "—" when there is no work left.
    /// </summary>
    private void RefreshForecast(PomodoroTimer timer, IslandSettings settings, bool planActive, ScheduledStart? pending)
    {
        // A pending start shows its own time while nothing runs; otherwise the forecast of the work left.
        bool showSchedule = pending is not null && !timer.IsRunning && !planActive;
        TimeSpan rest = showSchedule ? TimeSpan.Zero : timer.TimeToFinish(settings.PomodoroCycles);
        string key;
        if (showSchedule)
        {
            string at = pending!.At.ToLocalTime().ToString("HH:mm", PtBr);
            string label = (pending.Mode == ScheduledStartMode.Angry ? "Angry" : "Focus") + " ×" + pending.Cycles;
            key = $"às {at}|{label}|scheduled";
        }
        else if (rest <= TimeSpan.Zero)
        {
            key = "—";
        }
        else
        {
            string clock = (DateTime.Now + rest).ToString("HH:mm", PtBr);
            key = $"{clock}|{FormatDuration(rest)}|{planActive}";
        }

        if (key == _shownForecast) return;
        _shownForecast = key;

        ForecastText.Inlines.Clear();
        if (showSchedule)
        {
            bool angry = pending!.Mode == ScheduledStartMode.Angry;
            string at = pending.At.ToLocalTime().ToString("HH:mm", PtBr);
            string label = (angry ? "Angry" : "Focus") + " ×" + pending.Cycles;
            ForecastText.Inlines.Add(new Run("às ") { Foreground = Grey });
            ForecastText.Inlines.Add(new Run(at)
            {
                Foreground = angry ? Red : Orange,
                FontWeight = System.Windows.FontWeights.SemiBold,
            });
            ForecastText.Inlines.Add(new Run(" · " + label) { Foreground = Grey });
            return;
        }
        if (rest <= TimeSpan.Zero)
        {
            ForecastText.Inlines.Add(new Run("—") { Foreground = Grey });
            return;
        }

        string[] parts = key.Split('|');
        ForecastText.Inlines.Add(new Run("até ") { Foreground = Grey });
        ForecastText.Inlines.Add(new Run(parts[0])
        {
            Foreground = planActive ? Orange : White,
            FontWeight = System.Windows.FontWeights.SemiBold,
        });
        ForecastText.Inlines.Add(new Run(" · " + parts[1]) { Foreground = Grey });
    }

    /// <summary>"45 min" under an hour, "2h" on the hour, otherwise "1h30".</summary>
    private static string FormatDuration(TimeSpan span)
    {
        int minutes = Math.Max(1, (int)Math.Round(span.TotalMinutes));
        int hours = minutes / 60;
        int rest = minutes % 60;
        if (hours == 0) return $"{rest} min";
        return rest == 0 ? $"{hours}h" : $"{hours}h{rest:00}";
    }

    /// <summary>Opens the schedule dialog. Ignored while locked: the dimmed clock button does nothing then.</summary>
    private void OnScheduleClicked()
    {
        if (_ctx.Angry.IsLocked) return;
        ScheduleWindow.ShowFor(_ctx.Schedule, _ctx.Settings().PomodoroCycles);
    }

    /// <summary>Phase pills are ignored during a plan: the plan decides the phase. Choosing one clears Angry selection.</summary>
    private void SelectPhase(PomodoroPhase phase)
    {
        if (_ctx.Pomodoro.PlanActive) return;

        _angrySelected = false;
        _ctx.Pomodoro.SetPhase(phase);
        Refresh();
    }

    /// <summary>
    /// Session active (locked or on a free break): opens the unlock dialog that leaves the whole session.
    /// Otherwise selects Angry; the Play button starts the locked plan.
    /// </summary>
    private void OnAngryClicked()
    {
        if (_ctx.Angry.IsSessionActive) UnlockWindow.ShowFor(_ctx.Angry);
        else
        {
            _angrySelected = true;
            Refresh();
        }
    }

    private void OnPlayClicked()
    {
        if (_angrySelected)
        {
            _angrySelected = false;
            _ctx.Angry.Engage(_ctx.Settings().PomodoroCycles);
            Refresh();
            return;
        }

        _ctx.Pomodoro.Play(_ctx.Settings().PomodoroCycles);
    }

    /// <summary>
    /// − / + (and the wheel): during a plan the plan's total changes live and the setting follows it. Otherwise the
    /// setting changes within 1 to 24. Allowed while running and while locked.
    /// </summary>
    private void OnStepped(int delta)
    {
        PomodoroTimer timer = _ctx.Pomodoro;
        IslandSettings settings = _ctx.Settings();
        if (timer.PlanActive)
        {
            if (timer.SetPlanTotal(timer.TotalCycles + delta))
            {
                _ctx.ApplySettings(settings with { PomodoroCycles = timer.TotalCycles });
            }
        }
        else
        {
            int cycles = Math.Clamp(settings.PomodoroCycles + delta, PomodoroTimer.MinCycles, PomodoroTimer.MaxCycles);
            if (cycles != settings.PomodoroCycles) _ctx.ApplySettings(settings with { PomodoroCycles = cycles });
        }
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

    /// <summary>Selected or locked: red soft background. Active session or selected mode: red label.</summary>
    private void ApplyAngryPill(bool locked, bool sessionActive, bool selected)
    {
        AngryPill.Background = locked || selected ? RedSoft : Chip;
        AngryPill.LabelBrush = sessionActive || selected ? Red : Grey;
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

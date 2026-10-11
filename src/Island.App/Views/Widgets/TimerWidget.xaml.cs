using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Island.App.Widgets;
using Island.Core.Tools.Timers;
using Brush = System.Windows.Media.Brush;
using UserControl = System.Windows.Controls.UserControl;

namespace Island.App.Views.Widgets;

/// <summary>
/// Timer and stopwatch in one 300 x 152 card, switched by a sliding control on top. The countdown has a progress ring,
/// −/+ for one minute (the mouse wheel on the ring for ten seconds), a cycling preset, play/pause and reset. When it ends
/// the ring flashes green and says "Pronto!" for three seconds, then keeps breathing. The stopwatch shows centiseconds
/// and its two laps in view, with the best lap green and the worst red.
/// The models run on the clock, so the frame timer is started only while something runs and the widget is on screen.
/// </summary>
public partial class TimerWidget : UserControl
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan FlashDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PanelDuration = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TenSeconds = TimeSpan.FromSeconds(10);
    private const double PulseBelowSeconds = 10;
    private const double PulseDepth = 0.03;
    private const double PanelSlide = 12;

    private readonly ShelfContext _context;
    private readonly TimerTools _tools;
    private readonly UiSignal _signal;
    private readonly DispatcherTimer _frame = new() { Interval = FrameInterval };
    private readonly DispatcherTimer _flashTimer = new() { Interval = FlashDuration };
    private readonly Brush _orange;
    private readonly Brush _green;
    private bool _subscribed;
    private bool _finished;
    private bool _flashing;
    private bool _breathing;
    private int _lapOffset;

    public TimerWidget(ShelfContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        InitializeComponent();
        _context = context;
        // The alarm is wired here, not in Core, so the countdown can be tested without sound.
        _tools = context.Tools.GetOrCreate(() => new TimerTools(TimeProvider.System, SystemSounds.Asterisk.Play));
        _signal = new UiSignal(Dispatcher, Refresh);
        _orange = (Brush)FindResource("AccentOrangeBrush");
        _green = (Brush)FindResource("AccentGreenBrush");
        _frame.Tick += (_, _) => Render();
        _flashTimer.Tick += (_, _) => EndFlash();

        ModeSwitch.ModeSelected += SelectMode;
        MinusButton.Click += () => _tools.Countdown.Adjust(-OneMinute);
        PlusButton.Click += () => _tools.Countdown.Adjust(OneMinute);
        PresetButton.Click += () => _tools.Countdown.CyclePreset();
        CountdownPlayButton.Click += () => Toggle(_tools.Countdown.IsRunning, _tools.Countdown.Pause, _tools.Countdown.Start);
        CountdownResetButton.Click += () => _tools.Countdown.Reset();
        RingHost.MouseWheel += (_, e) =>
        {
            _tools.Countdown.Adjust(e.Delta > 0 ? TenSeconds : -TenSeconds);
            e.Handled = true;
        };

        StopwatchPlayButton.Click += () => Toggle(_tools.Stopwatch.IsRunning, _tools.Stopwatch.Pause, _tools.Stopwatch.Start);
        LapButton.Click += () =>
        {
            _tools.Stopwatch.Lap();
            _lapOffset = 0;
        };
        StopwatchResetButton.Click += () =>
        {
            _tools.Stopwatch.Reset();
            _lapOffset = 0;
        };
        // Handled even at the end of the list, so a wheel over the laps does not turn the shelf page.
        Laps.MouseWheel += (_, e) =>
        {
            ScrollLaps(e.Delta < 0 ? 1 : -1);
            e.Handled = true;
        };

        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
        IsVisibleChanged += (_, _) => Refresh();
    }

    private bool ReduceAnimations => _context.Settings().ReduceAnimations;

    private void Subscribe()
    {
        if (_subscribed) return;

        _subscribed = true;
        _tools.Countdown.Changed += OnModelChanged;
        _tools.Stopwatch.Changed += OnModelChanged;
        // A countdown that ended while the widget was away keeps its finished look, without replaying the flash.
        _finished = _tools.Countdown.State == CountdownState.Finished;
        Ring.Tone = _orange;
        ShowMode(animate: false);
        Refresh();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        _subscribed = false;
        _tools.Countdown.Changed -= OnModelChanged;
        _tools.Stopwatch.Changed -= OnModelChanged;
        _frame.Stop();
        _flashTimer.Stop();
        _flashing = false;
        _breathing = false;
        Ring.StopBreathing();
    }

    /// <summary>Arrives on any thread; the work runs on the UI thread through <see cref="UiSignal"/>.</summary>
    private void OnModelChanged() => _signal.Signal();

    private void SelectMode(TimerMode mode)
    {
        if (_tools.Mode == mode) return;

        _tools.SetMode(mode);
        ShowMode(animate: true);
        Refresh();
    }

    /// <summary>Applies the models' current state to the controls.</summary>
    private void Refresh()
    {
        bool finished = _tools.Countdown.State == CountdownState.Finished;
        if (finished != _finished)
        {
            _finished = finished;
            if (finished) StartFinishLook();
            else StopFinishLook();
        }

        RenderCountdown(jump: true);
        RenderStopwatch();
        RenderLaps();
        SyncMotion();
    }

    /// <summary>Frame tick: only the running tool needs redrawing; the ring is set at once, not animated.</summary>
    private void Render()
    {
        if (_tools.Mode == TimerMode.Countdown) RenderCountdown(jump: false);
        else RenderStopwatch();
    }

    private void RenderCountdown(bool jump)
    {
        CountdownModel countdown = _tools.Countdown;
        TimeSpan remaining = countdown.Remaining;
        bool running = countdown.IsRunning;
        bool reduce = ReduceAnimations;

        Ring.SetProgress(countdown.Progress, animate: jump && !reduce);
        string text = _flashing ? "Pronto!" : TimerFormat.Countdown(remaining);
        if (CountdownText.Text != text) CountdownText.Text = text;

        // Pulses once per second in the last ten seconds, peaking as the displayed number changes.
        bool pulsing = running && !reduce && remaining.TotalSeconds < PulseBelowSeconds;
        double scale = pulsing ? 1 + PulseDepth * Pulse(remaining) : 1;
        Ring.SetScale(scale);
        CountdownScale.ScaleX = scale;
        CountdownScale.ScaleY = scale;

        CountdownPlayButton.IsAltShown = running;
        MinusButton.IsEnabled = !running;
        PlusButton.IsEnabled = !running;
        PresetButton.IsEnabled = !running;
        string preset = TimerFormat.Duration(countdown.Duration);
        if (PresetButton.LabelText != preset) PresetButton.LabelText = preset;
    }

    private void RenderStopwatch()
    {
        StopwatchModel stopwatch = _tools.Stopwatch;
        string text = TimerFormat.Stopwatch(stopwatch.Elapsed);
        if (StopwatchText.Text != text) StopwatchText.Text = text;

        StopwatchPlayButton.IsAltShown = stopwatch.IsRunning;
        LapButton.IsEnabled = stopwatch.IsRunning;
    }

    private void RenderLaps()
    {
        StopwatchModel stopwatch = _tools.Stopwatch;
        IReadOnlyList<LapRecord> laps = stopwatch.Laps;
        _lapOffset = Math.Clamp(_lapOffset, 0, TimerLapRows.MaxOffset(laps.Count));
        Laps.Show(laps, _lapOffset, stopwatch.BestLap, stopwatch.WorstLap);
    }

    private void ScrollLaps(int step)
    {
        _lapOffset += step;
        RenderLaps();
    }

    /// <summary>
    /// Runs the frame timer only while the visible tool is running, and the breathing ring only while a finished
    /// countdown is on screen. Nothing is animated when the widget is hidden or unloaded.
    /// </summary>
    private void SyncMotion()
    {
        bool shown = IsLoaded && IsVisible;
        bool running = _tools.Mode == TimerMode.Countdown ? _tools.Countdown.IsRunning : _tools.Stopwatch.IsRunning;
        bool ticking = shown && running;
        if (ticking && !_frame.IsEnabled) _frame.Start();
        else if (!ticking && _frame.IsEnabled) _frame.Stop();

        bool breathing = shown && _finished && !ReduceAnimations;
        if (breathing == _breathing) return;

        _breathing = breathing;
        if (breathing) Ring.StartBreathing();
        else Ring.StopBreathing();
    }

    private void StartFinishLook()
    {
        _flashing = true;
        Ring.Tone = _green;
        _flashTimer.Start();
    }

    private void StopFinishLook()
    {
        _flashTimer.Stop();
        _flashing = false;
        Ring.Tone = _orange;
    }

    private void EndFlash()
    {
        _flashTimer.Stop();
        _flashing = false;
        Ring.Tone = _orange;
        RenderCountdown(jump: false);
    }

    /// <summary>Shows the panel of the current mode. Switching slides the new one in and fades the old one out.</summary>
    private void ShowMode(bool animate)
    {
        bool stopwatch = _tools.Mode == TimerMode.Stopwatch;
        bool move = animate && !ReduceAnimations;
        ModeSwitch.Select(_tools.Mode, move);

        FrameworkElement incoming = stopwatch ? StopwatchPanel : CountdownPanel;
        FrameworkElement outgoing = stopwatch ? CountdownPanel : StopwatchPanel;
        TranslateTransform incomingOffset = stopwatch ? StopwatchOffset : CountdownOffset;
        TranslateTransform outgoingOffset = stopwatch ? CountdownOffset : StopwatchOffset;
        double direction = stopwatch ? 1 : -1;

        incoming.Visibility = Visibility.Visible;
        if (!move)
        {
            Settle(incoming, UIElement.OpacityProperty, 1);
            Settle(incomingOffset, TranslateTransform.XProperty, 0);
            Settle(outgoing, UIElement.OpacityProperty, 1);
            Settle(outgoingOffset, TranslateTransform.XProperty, 0);
            outgoing.Visibility = Visibility.Collapsed;
            return;
        }

        Settle(incoming, UIElement.OpacityProperty, 0);
        Settle(incomingOffset, TranslateTransform.XProperty, PanelSlide * direction);
        Animate(incoming, UIElement.OpacityProperty, 0, 1);
        Animate(incomingOffset, TranslateTransform.XProperty, PanelSlide * direction, 0);
        Animate(outgoing, UIElement.OpacityProperty, 1, 0, () => outgoing.Visibility = Visibility.Collapsed);
        Animate(outgoingOffset, TranslateTransform.XProperty, 0, -PanelSlide * direction);
    }

    private static void Toggle(bool running, Action pause, Action start)
    {
        if (running) pause();
        else start();
    }

    /// <summary>A 0 to 1 wave that peaks on each whole second of the remaining time.</summary>
    private static double Pulse(TimeSpan remaining)
    {
        double phase = remaining.TotalSeconds % 1;
        return 0.5 + 0.5 * Math.Cos(2 * Math.PI * phase);
    }

    /// <summary>Removes any running animation first, so the value set here is the one shown.</summary>
    private static void Settle<T>(T target, DependencyProperty property, double value)
        where T : DependencyObject, IAnimatable
    {
        target.BeginAnimation(property, null);
        target.SetValue(property, value);
    }

    private static void Animate<T>(T target, DependencyProperty property, double from, double to, Action? done = null)
        where T : DependencyObject, IAnimatable
    {
        var animation = new DoubleAnimation(from, to, PanelDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) =>
        {
            Settle(target, property, to);
            done?.Invoke();
        };
        target.BeginAnimation(property, animation);
    }
}

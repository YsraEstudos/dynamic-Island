using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Island.Core.Pomodoro;
using Island.Windows.Input;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Island.App.Shell;

/// <summary>A six-second interlude. It observes a completed transition and never controls the timer.</summary>
public partial class PomodoroTransitionWindow : Window
{
    private const double LifetimeSeconds = 6;
    private readonly bool _rest;
    private readonly bool _reduce;
    private readonly string _title;
    private readonly DispatcherTimer _ticker;
    private readonly Stopwatch _elapsed = new();
    private readonly SolidColorBrush _headlineBrush;
    private int _characters = -1;
    private readonly Action? _startNext;
    private EscapeKeyWatcher? _escapeWatcher;
    private bool _closed;

    public PomodoroTransitionWindow(PomodoroTransition transition, bool reduceAnimations)
        : this(transition, reduceAnimations, string.Empty, null) { }

    public PomodoroTransitionWindow(PomodoroTransition transition, bool reduceAnimations, string goal, Action? startNext)
    {
        InitializeComponent();
        _startNext = startNext;
        _rest = transition.Next == PomodoroPhase.Break;
        _reduce = reduceAnimations || !SystemParameters.ClientAreaAnimation;
        _title = _rest ? "Hora de descansar" : "Hora de estudar";
        Title = _title;
        if (!_rest && !string.IsNullOrWhiteSpace(goal))
        {
            SessionGoalText.Text = PomodoroProfiles.CleanText(goal, 160);
            SessionGoalText.Visibility = Visibility.Visible;
        }
        if (!transition.AutoStarted && startNext is not null)
        {
            StartNextButton.Visibility = Visibility.Visible;
            StartNextButton.Content = _rest ? "Iniciar descanso" : "Iniciar foco";
            FooterHint.Text = "A próxima etapa aguarda Iniciar na ilha · Esc fecha este aviso";
        }
        Coffee.Visibility = _rest ? Visibility.Visible : Visibility.Collapsed;
        Study.Visibility = _rest ? Visibility.Collapsed : Visibility.Visible;
        Eyebrow.Text = _rest ? "PAUSA MERECIDA" : "UM PASSO DE CADA VEZ";
        int minutes = (int)Math.Round(transition.NextDuration.TotalMinutes);
        Subtitle.Text = _rest ? $"Respire, alongue-se. Você tem {minutes} minutos de pausa."
            : transition.TotalCycles > 0
                ? $"Pomodoro {transition.Cycle + 1} de {transition.TotalCycles} · {minutes} minutos de foco."
                : $"Seu próximo foco: {minutes} minutos.";
        if (!_rest) Glow.Color = Color.FromRgb(0x2D, 0x1B, 0x24);
        _headlineBrush = new SolidColorBrush(_rest ? Color.FromRgb(255, 241, 219) : Color.FromRgb(255, 105, 114));
        Headline.Foreground = _headlineBrush;
        // 20 fps is enough for steam and the colour cycle; the full-monitor overlay costs most when redrawn.
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_reduce ? 100 : 50) };
        _ticker.Tick += OnTick;
        Loaded += (_, _) =>
        {
            StartGlobalEscape();
            CloseButton.Focus();
            _elapsed.Restart();
            UpdateFrame();
            _ticker.Start();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _ticker.Stop();
            _ticker.Tick -= OnTick;
            _elapsed.Stop();
            _escapeWatcher?.Dispose();
            _escapeWatcher = null;
        };
    }

    /// <summary>
    /// Activation from a timer is often refused by Windows, so this overlay may have no keyboard focus and
    /// PreviewKeyDown never fires. The global watcher catches Esc while the overlay is open; PreviewKeyDown stays as the in-window path.
    /// </summary>
    private void StartGlobalEscape()
    {
        Dispatcher dispatcher = Dispatcher;
        _escapeWatcher = new EscapeKeyWatcher(() => dispatcher.BeginInvoke(new Action(CloseFromEscape)));
        _escapeWatcher.Start();
    }

    private void CloseFromEscape()
    {
        if (!_closed) Close();
    }

    /// <summary>Uses the island's actual monitor, including after dragging it, in physical pixels.</summary>
    public void ShowOnIslandMonitor(Window island)
    {
        var bounds = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(island).Handle).Bounds;
        Owner = island;
        SourceInitialized += (_, _) => SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1),
            bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040 /* SWP_SHOWWINDOW */);
        Show();
        // Show and a per-monitor DPI change can apply WPF's cached DIP bounds. Reassert the physical rectangle.
        SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1),
            bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040);
        Activate();
        Focus();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_elapsed.Elapsed.TotalSeconds >= LifetimeSeconds) { Close(); return; }
        UpdateFrame();
    }

    private void UpdateFrame()
    {
        double seconds = _elapsed.Elapsed.TotalSeconds;
        int count = _reduce ? _title.Length : Math.Clamp((int)((seconds - 0.35) / 0.055), 0, _title.Length);
        if (count != _characters) { Headline.Text = _title[..count]; _characters = count; }
        LifetimeScale.ScaleX = Math.Max(0, 1 - seconds / LifetimeSeconds);
        double caret = _reduce ? 0 : (int)(seconds / 0.55) % 2 == 0 ? 1 : 0;
        if (Caret.Opacity != caret) Caret.Opacity = caret;
        if (_reduce) return;

        // Entrance and exit are the only times these move. Between them the values are 1 and 0, so skip the no-op writes;
        // every write to Surface re-rasterises the whole content group.
        double enter = 1 - Math.Pow(1 - Math.Clamp(seconds / 0.65, 0, 1), 3);
        double surfaceOpacity = enter * Math.Clamp((LifetimeSeconds - seconds) / 0.4, 0, 1);
        if (Surface.Opacity != surfaceOpacity) Surface.Opacity = surfaceOpacity;
        double entranceY = 22 * (1 - enter);
        if (Entrance.Y != entranceY) Entrance.Y = entranceY;
        if (_rest)
        {
            AnimateSteam(SteamOne, SteamOneMove, seconds, 0);
            AnimateSteam(SteamTwo, SteamTwoMove, seconds, 0.7);
            AnimateSteam(SteamThree, SteamThreeMove, seconds, 1.4);
        }
        else
        {
            // One soft red/white cycle every 1.8 s, confined to the lettering.
            double mix = (1 - Math.Cos(seconds * Math.PI / 0.9)) / 2;
            _headlineBrush.Color = Color.FromRgb(255, (byte)(105 + 150 * mix), (byte)(114 + 141 * mix));
        }
    }

    private static void AnimateSteam(UIElement steam, TranslateTransform move, double seconds, double offset)
    {
        double phase = ((seconds + offset) % 2.8) / 2.8;
        move.Y = -18 * phase;
        move.X = Math.Sin(phase * Math.PI * 2) * 4;
        steam.Opacity = Math.Sin(phase * Math.PI) * 0.65;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        e.Handled = true;
        CloseFromEscape();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
    private void OnStartNext(object sender, RoutedEventArgs e) { _startNext?.Invoke(); Close(); }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.App.Animations;
using Island.App.Widgets;
using Island.Core.Tools.Timers;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Island.App.Views.Widgets;

/// <summary>
/// Two-option switch for the timer and the stopwatch. A pill slides under the chosen option on a damped spring, so a
/// quick second choice keeps the pill's momentum. Only the spring is animated, and only while the pill moves.
/// </summary>
public sealed class TimerModeSwitch : Border, IFrameClient
{
    private const double Inset = 2;
    private const double Zeta = 0.8;
    private const double SettleSeconds = 0.3;

    private static readonly Brush TileBrush = Frozen(0x1C, 0x1C, 0x1E);
    private static readonly Brush PillBrush = Frozen(0x3A, 0x3A, 0x3C);
    private static readonly Brush SelectedLabel = Frozen(0xFF, 0xFF, 0xFF);
    private static readonly Brush IdleLabel = Frozen(0xA1, 0xA1, 0xA6);

    private readonly Border _pill = new()
    {
        CornerRadius = new CornerRadius(11),
        Background = PillBrush,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(Inset),
        IsHitTestVisible = false,
    };
    private readonly TranslateTransform _pillOffset = new();
    private readonly SpringValue _position = new(SpringValue.OmegaForSettleTime(SettleSeconds, Zeta), Zeta, 0.0);
    private readonly ShelfButton _countdownButton = new() { LabelText = "Temporizador" };
    private readonly ShelfButton _stopwatchButton = new() { LabelText = "Cronômetro" };
    private TimerMode _selected = TimerMode.Countdown;
    private double _segmentWidth;

    public TimerModeSwitch()
    {
        Background = TileBrush;
        CornerRadius = new CornerRadius(13);

        _pill.RenderTransform = _pillOffset;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(_pill);
        Grid.SetColumn(_countdownButton, 0);
        Grid.SetColumn(_stopwatchButton, 1);
        grid.Children.Add(_countdownButton);
        grid.Children.Add(_stopwatchButton);
        Child = grid;

        _countdownButton.Click += () => ModeSelected?.Invoke(TimerMode.Countdown);
        _stopwatchButton.Click += () => ModeSelected?.Invoke(TimerMode.Stopwatch);
        SizeChanged += (_, _) =>
        {
            _segmentWidth = ActualWidth / 2;
            _pill.Width = Math.Max(0, _segmentWidth - 2 * Inset);
            ApplyPosition();
        };
        UpdateLabels();
    }

    /// <summary>Raised when the user picks an option.</summary>
    public event Action<TimerMode>? ModeSelected;

    /// <summary>Moves the pill under <paramref name="mode"/>. Without <paramref name="animate"/> it jumps.</summary>
    public void Select(TimerMode mode, bool animate)
    {
        _selected = mode;
        UpdateLabels();

        double target = mode == TimerMode.Stopwatch ? 1 : 0;
        if (animate)
        {
            _position.SetTarget(target);
            MotionPump.Request(this);
            return;
        }

        _position.Snap(target);
        ApplyPosition();
    }

    public bool OnFrame(double dt)
    {
        bool settled = _position.Advance(dt);
        ApplyPosition();
        return !settled;
    }

    private void ApplyPosition() => _pillOffset.X = _position.Current * _segmentWidth;

    private void UpdateLabels()
    {
        _countdownButton.LabelBrush = _selected == TimerMode.Countdown ? SelectedLabel : IdleLabel;
        _stopwatchButton.LabelBrush = _selected == TimerMode.Stopwatch ? SelectedLabel : IdleLabel;
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.App.Animations;

namespace Island.App.Controls;

/// <summary>
/// Small four-bar equalizer. Static by default (no frame callbacks). <see cref="SetAnimating"/> springs the
/// bars toward random heights; it must only be switched on while the glyph is visible and the media is playing.
/// </summary>
public sealed class EqualizerGlyph : System.Windows.Controls.StackPanel, IFrameClient
{
    private const int BarCount = 4;
    private const double BarWidth = 3.0;
    private const double BarGap = 2.5;
    private const double BarHeight = 14.0;
    private const double SettleSeconds = 0.22;
    private const double Zeta = 0.80;

    private static readonly double[] StaticLevels = { 0.45, 1.0, 0.65, 0.35 };

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(System.Windows.Media.Brush), typeof(EqualizerGlyph),
        new PropertyMetadata(System.Windows.Media.Brushes.White, OnBarBrushChanged));

    private readonly System.Windows.Controls.Border[] _bars = new System.Windows.Controls.Border[BarCount];
    private readonly ScaleTransform[] _scales = new ScaleTransform[BarCount];
    private readonly SpringValue[] _springs = new SpringValue[BarCount];
    private readonly double[] _nextRetargetAt = new double[BarCount];
    private readonly Random _random = new();
    private bool _animating;
    private double _clock;

    public EqualizerGlyph()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal;
        VerticalAlignment = System.Windows.VerticalAlignment.Center;
        Height = BarHeight;

        for (int i = 0; i < BarCount; i++)
        {
            _scales[i] = new ScaleTransform(1.0, StaticLevels[i]);
            _bars[i] = new System.Windows.Controls.Border
            {
                Width = BarWidth,
                Height = BarHeight,
                CornerRadius = new CornerRadius(BarWidth / 2.0),
                Margin = i == BarCount - 1 ? new Thickness(0) : new Thickness(0, 0, BarGap, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                RenderTransformOrigin = new System.Windows.Point(0.5, 1.0),
                RenderTransform = _scales[i],
                Background = BarBrush,
            };
            Children.Add(_bars[i]);
            _springs[i] = new SpringValue(SpringValue.OmegaForSettleTime(SettleSeconds, Zeta), Zeta, StaticLevels[i]);
        }
    }

    public System.Windows.Media.Brush BarBrush
    {
        get => (System.Windows.Media.Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    /// <summary>Starts or stops the looping motion. Stopping returns the bars to their static heights.</summary>
    public void SetAnimating(bool animating)
    {
        if (_animating == animating) return;
        _animating = animating;

        if (animating)
        {
            for (int i = 0; i < BarCount; i++) _nextRetargetAt[i] = _clock;
        }
        else
        {
            for (int i = 0; i < BarCount; i++) _springs[i].SetTarget(StaticLevels[i]);
        }
        MotionPump.Request(this);
    }

    public bool OnFrame(double dt)
    {
        _clock += dt;
        bool busy = false;

        for (int i = 0; i < BarCount; i++)
        {
            if (_animating && _clock >= _nextRetargetAt[i])
            {
                _springs[i].SetTarget(0.25 + 0.75 * _random.NextDouble());
                _nextRetargetAt[i] = _clock + 0.18 + 0.22 * _random.NextDouble();
            }

            bool settled = _springs[i].Advance(dt);
            _scales[i].ScaleY = _springs[i].Current;
            if (!settled) busy = true;
        }

        return _animating || busy;
    }

    private static void OnBarBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var glyph = (EqualizerGlyph)d;
        foreach (System.Windows.Controls.Border bar in glyph._bars)
        {
            bar.Background = (System.Windows.Media.Brush)e.NewValue;
        }
    }
}

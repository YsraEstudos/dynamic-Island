using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Island.App.Animations;

namespace Island.App.Controls;

/// <summary>
/// Bar showing a 0..1 fraction, horizontal by default or vertical (fills from the bottom up). The fill springs
/// toward <see cref="Value"/>.
/// When <see cref="Interactive"/> is set, the user can drag it: while dragging the fill follows the cursor
/// directly. Releasing inside the bar raises <see cref="Seeked"/>; releasing outside cancels and the fill
/// springs back to the real value.
/// </summary>
public sealed class FractionBar : System.Windows.Controls.Grid, IFrameClient
{
    /// <summary>The spring works in per-mille so its settle thresholds are meaningful at bar scale.</summary>
    private const double SpringScale = 1000.0;
    private const double SettleSeconds = 0.28;
    private const double Zeta = 0.85;
    private const double HitSlop = 6.0;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(FractionBar), new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty BarThicknessProperty = DependencyProperty.Register(
        nameof(BarThickness), typeof(double), typeof(FractionBar), new PropertyMetadata(4.0, OnBarThicknessChanged));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush), typeof(System.Windows.Media.Brush), typeof(FractionBar),
        new PropertyMetadata(System.Windows.Media.Brushes.White, OnBrushChanged));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(System.Windows.Media.Brush), typeof(FractionBar),
        new PropertyMetadata(System.Windows.Media.Brushes.Gray, OnBrushChanged));

    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.Register(
        nameof(Interactive), typeof(bool), typeof(FractionBar), new PropertyMetadata(false));

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(System.Windows.Controls.Orientation), typeof(FractionBar),
        new PropertyMetadata(System.Windows.Controls.Orientation.Horizontal, OnOrientationChanged));

    private readonly System.Windows.Controls.Border _track;
    private readonly System.Windows.Controls.Border _fill;
    private readonly SpringValue _spring = new(SpringValue.OmegaForSettleTime(SettleSeconds, Zeta), Zeta, 0.0);

    private bool _dragging;
    private double _dragFraction;

    public FractionBar()
    {
        Background = System.Windows.Media.Brushes.Transparent;   // Hit-test surface for dragging.
        ClipToBounds = false;

        _track = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(2),
            Height = BarThickness,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            Background = TrackBrush,
        };
        _fill = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(2),
            Height = BarThickness,
            Width = 0,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Background = FillBrush,
        };
        Children.Add(_track);
        Children.Add(_fill);

        SizeChanged += (_, _) => LayoutFill(_spring.Current);
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouse;
        MouseLeftButtonUp += OnMouseUp;
    }

    /// <summary>Fraction 0..1 to show.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double BarThickness
    {
        get => (double)GetValue(BarThicknessProperty);
        set => SetValue(BarThicknessProperty, value);
    }

    public System.Windows.Media.Brush FillBrush
    {
        get => (System.Windows.Media.Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public System.Windows.Media.Brush TrackBrush
    {
        get => (System.Windows.Media.Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>When true the bar can be dragged to pick a fraction.</summary>
    public bool Interactive
    {
        get => (bool)GetValue(InteractiveProperty);
        set => SetValue(InteractiveProperty, value);
    }

    /// <summary>Horizontal (left to right, default) or Vertical (bottom to top).</summary>
    public System.Windows.Controls.Orientation Orientation
    {
        get => (System.Windows.Controls.Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    private bool IsVertical => Orientation == System.Windows.Controls.Orientation.Vertical;

    /// <summary>Raised with the chosen fraction when a drag is released inside the bar.</summary>
    public event Action<double>? Seeked;

    /// <summary>Raised with true when a drag starts and false when it ends (inside or outside).</summary>
    public event Action<bool>? DraggingChanged;

    public bool OnFrame(double dt)
    {
        bool settled = _spring.Advance(dt);
        LayoutFill(_spring.Current);
        return !settled;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (FractionBar)d;
        if (bar._dragging) return;
        double fraction = Math.Clamp((double)e.NewValue, 0.0, 1.0);
        bar._spring.SetTarget(fraction * SpringScale);
        MotionPump.Request(bar);
    }

    private static void OnBarThicknessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((FractionBar)d).ApplyOrientation();
    }

    private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((FractionBar)d).ApplyOrientation();
    }

    /// <summary>Sets the track and fill sizes and alignment for the current orientation and thickness.</summary>
    private void ApplyOrientation()
    {
        double thickness = BarThickness;
        if (IsVertical)
        {
            _track.Width = thickness;
            _track.Height = double.NaN;
            _track.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            _track.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            _fill.Width = thickness;
            _fill.Height = 0;
            _fill.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            _fill.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
        }
        else
        {
            _track.Height = thickness;
            _track.Width = double.NaN;
            _track.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            _track.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            _fill.Height = thickness;
            _fill.Width = 0;
            _fill.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            _fill.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        }
        LayoutFill(_spring.Current);
    }

    private static void OnBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (FractionBar)d;
        bar._track.Background = bar.TrackBrush;
        bar._fill.Background = bar.FillBrush;
    }

    private void LayoutFill(double perMille)
    {
        double fraction = Math.Clamp(perMille / SpringScale, 0.0, 1.0);
        if (IsVertical)
        {
            _fill.Height = Math.Max(0.0, ActualHeight * fraction);
        }
        else
        {
            _fill.Width = Math.Max(0.0, ActualWidth * fraction);
        }
    }

    private double FractionAt(System.Windows.Point position)
    {
        if (IsVertical)
        {
            return ActualHeight <= 0.0 ? 0.0 : Math.Clamp((ActualHeight - position.Y) / ActualHeight, 0.0, 1.0);
        }
        return ActualWidth <= 0.0 ? 0.0 : Math.Clamp(position.X / ActualWidth, 0.0, 1.0);
    }

    private void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        double length = IsVertical ? ActualHeight : ActualWidth;
        if (!Interactive || length <= 0.0) return;

        _dragging = true;
        CaptureMouse();
        _dragFraction = FractionAt(e.GetPosition(this));
        _spring.Snap(_dragFraction * SpringScale);
        LayoutFill(_spring.Current);
        DraggingChanged?.Invoke(true);
        e.Handled = true;
    }

    private void OnMouse(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) return;

        _dragFraction = FractionAt(e.GetPosition(this));
        _spring.Snap(_dragFraction * SpringScale);
        LayoutFill(_spring.Current);
    }

    private void OnMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();
        System.Windows.Point position = e.GetPosition(this);
        bool inside = IsVertical
            ? position.Y >= 0.0 && position.Y <= ActualHeight
              && position.X >= -HitSlop && position.X <= ActualWidth + HitSlop
            : position.X >= 0.0 && position.X <= ActualWidth
              && position.Y >= -HitSlop && position.Y <= ActualHeight + HitSlop;

        if (inside)
        {
            _spring.SetTarget(_dragFraction * SpringScale);
            MotionPump.Request(this);
            Seeked?.Invoke(_dragFraction);
        }
        else
        {
            // Released outside: cancel the drag and spring back to the real value.
            _spring.SetTarget(Math.Clamp(Value, 0.0, 1.0) * SpringScale);
            MotionPump.Request(this);
        }

        DraggingChanged?.Invoke(false);
        e.Handled = true;
    }
}

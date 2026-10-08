using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Island.App.Animations;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Island.App.Widgets;

/// <summary>
/// Horizontal minute ruler: a tick per minute (every fifth taller with its number above) and a fixed triangular marker
/// at the centre. Drag it directly: the strip follows the pointer, the value is the integer minute under the marker and
/// <see cref="ValueDragged"/> fires whenever that integer changes. On release the strip springs so the chosen minute is
/// centred and <see cref="ValueCommitted"/> fires. Set <see cref="Editable"/> to false to ignore drags.
/// </summary>
public sealed class MinuteRuler : FrameworkElement, IFrameClient
{
    public const int MinMinutes = 1;
    public const int MaxMinutes = 120;

    private const double PixelsPerMinute = 12.0;
    private const double MinorTickTop = 20.0;
    private const double MajorTickTop = 17.0;
    private const double TickBottom = 32.0;
    private const double MarkerTop = 36.0;
    private const double MarkerBottom = 43.0;
    private const double LabelEmSize = 11.0;
    private const double SpringSettleSeconds = 0.30;
    private const double SpringZeta = 0.85;

    private static readonly Color Orange = Color.FromRgb(0xFF, 0x9F, 0x0A);

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(MinuteRuler), new PropertyMetadata(25, OnValueChanged));

    public static readonly DependencyProperty EditableProperty = DependencyProperty.Register(
        nameof(Editable), typeof(bool), typeof(MinuteRuler), new PropertyMetadata(true, OnEditableChanged));

    private readonly SpringValue _center = new(SpringValue.OmegaForSettleTime(SpringSettleSeconds, SpringZeta), SpringZeta, 25.0);
    private readonly Typeface _typeface;
    private readonly Brush _whiteBrush = Brushes.White;
    private readonly Brush _markerBrush;

    private bool _dragging;
    private double _dragStartX;
    private double _dragStartCenter;
    private int _lastReported;

    public MinuteRuler()
    {
        ClipToBounds = true;
        Cursor = Cursors.SizeWE;

        FontFamily family = System.Windows.Application.Current?.TryFindResource("IslandFontFamily") as FontFamily
                            ?? new FontFamily("Segoe UI");
        _typeface = new Typeface(family, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

        var marker = new SolidColorBrush(Orange);
        marker.Freeze();
        _markerBrush = marker;

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
    }

    /// <summary>Raised with the integer minute under the marker each time it changes during a drag.</summary>
    public event Action<int>? ValueDragged;

    /// <summary>Raised with the chosen minute when a drag is released.</summary>
    public event Action<int>? ValueCommitted;

    /// <summary>Minute centred under the marker (1..120). Setting it springs the ruler to that value (unless dragging).</summary>
    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>When false the ruler ignores drags (it still shows the value).</summary>
    public bool Editable
    {
        get => (bool)GetValue(EditableProperty);
        set => SetValue(EditableProperty, value);
    }

    public bool OnFrame(double dt)
    {
        bool settled = _center.Advance(dt);
        InvalidateVisual();
        return !settled;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        if (width <= 0.0) return;

        // Transparent full-size hit surface so the empty area still takes the drag.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, ActualHeight));

        double center = _center.Current;
        double mid = width / 2.0;
        double half = mid / PixelsPerMinute;
        int first = Math.Max(MinMinutes, (int)Math.Floor(center - half) - 1);
        int last = Math.Min(MaxMinutes, (int)Math.Ceiling(center + half) + 1);
        int nearest = (int)Math.Round(Math.Clamp(center, MinMinutes, MaxMinutes));
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (int minute = first; minute <= last; minute++)
        {
            double x = mid + (minute - center) * PixelsPerMinute;
            double distance = Math.Abs(minute - center) / half;
            double alpha = Math.Clamp(1.0 - 0.85 * distance, 0.12, 1.0);
            Brush tick = Tint(alpha);

            bool major = minute % 5 == 0;
            double top = major ? MajorTickTop : MinorTickTop;
            dc.DrawRectangle(tick, null, new Rect(x - 0.75, top, 1.5, TickBottom - top));

            if (!major) continue;

            Brush labelBrush = minute == nearest ? _whiteBrush : tick;
            var label = new FormattedText(
                minute.ToString(CultureInfo.InvariantCulture),
                CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                _typeface,
                LabelEmSize,
                labelBrush,
                pixelsPerDip);
            dc.DrawText(label, new Point(x - label.Width / 2.0, 0.0));
        }

        var triangle = new StreamGeometry();
        using (StreamGeometryContext g = triangle.Open())
        {
            g.BeginFigure(new Point(mid, MarkerTop), isFilled: true, isClosed: true);
            g.LineTo(new Point(mid + 4.5, MarkerBottom), isStroked: false, isSmoothJoin: false);
            g.LineTo(new Point(mid - 4.5, MarkerBottom), isStroked: false, isSmoothJoin: false);
        }
        triangle.Freeze();
        dc.DrawGeometry(_markerBrush, null, triangle);
    }

    private static Brush Tint(double alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255.0), Orange.R, Orange.G, Orange.B));
        brush.Freeze();
        return brush;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ruler = (MinuteRuler)d;
        if (ruler._dragging) return;

        double target = Math.Clamp((int)e.NewValue, MinMinutes, MaxMinutes);
        if (ruler.IsLoaded)
        {
            ruler._center.SetTarget(target);
            MotionPump.Request(ruler);
        }
        else
        {
            ruler._center.Snap(target);
        }
        ruler.InvalidateVisual();
    }

    private static void OnEditableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ruler = (MinuteRuler)d;
        ruler.Cursor = (bool)e.NewValue ? Cursors.SizeWE : null;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (!Editable) return;

        _dragging = true;
        _dragStartX = e.GetPosition(this).X;
        _dragStartCenter = _center.Current;
        _lastReported = (int)Math.Round(_center.Current);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;

        double x = e.GetPosition(this).X;
        // Content follows the pointer: moving right brings smaller minutes under the marker.
        double center = Math.Clamp(_dragStartCenter - (x - _dragStartX) / PixelsPerMinute, MinMinutes, MaxMinutes);
        _center.Snap(center);

        int minute = (int)Math.Round(center);
        if (minute != _lastReported)
        {
            _lastReported = minute;
            ValueDragged?.Invoke(minute);
        }
        InvalidateVisual();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();

        int minute = (int)Math.Round(Math.Clamp(_center.Current, MinMinutes, MaxMinutes));
        _center.SetTarget(minute);
        MotionPump.Request(this);
        SetValue(ValueProperty, minute);
        e.Handled = true;

        ValueCommitted?.Invoke(minute);
    }
}

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Island.App.Mixer;

/// <summary>
/// Thin volume slider for one app: a 3 DIP track with a small round thumb, moved by dragging. Only a drag raises
/// <see cref="UserChanged"/>; setting <see cref="Value"/> from code (a refresh) does not, so the list cannot echo its own
/// updates back to the mixer. Mouse wheel is left to the list.
/// </summary>
public sealed class VolumeSlider : FrameworkElement
{
    private const double TrackHeight = 3.0;
    private const double ThumbRadius = 4.5;
    private const double TrackCorner = 1.5;

    private static readonly Brush TrackBrush = Frozen(0x3A, 0x3A, 0x3C);
    private static readonly Brush FillBrush = Frozen(0xE5, 0xE5, 0xEA);
    private static readonly Brush ThumbBrush = Frozen(0xFF, 0xFF, 0xFF);

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(VolumeSlider),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    private bool _dragging;

    public VolumeSlider()
    {
        Cursor = Cursors.Hand;
    }

    /// <summary>Level 0-100.</summary>
    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, 0, 100));
    }

    /// <summary>True while the thumb is held, so a refresh does not move it away from the pointer.</summary>
    public bool IsDragging => _dragging;

    /// <summary>Raised with the new level while the user drags.</summary>
    public event Action<int>? UserChanged;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        double top = (height - TrackHeight) / 2.0;
        double thumbX = ThumbX(Value, width);
        var track = new Rect(0, top, width, TrackHeight);
        var fill = new Rect(0, top, Math.Max(0, thumbX), TrackHeight);

        dc.DrawRoundedRectangle(TrackBrush, null, track, TrackCorner, TrackCorner);
        if (fill.Width > 0) dc.DrawRoundedRectangle(FillBrush, null, fill, TrackCorner, TrackCorner);
        dc.DrawEllipse(ThumbBrush, null, new Point(thumbX, height / 2.0), ThumbRadius, ThumbRadius);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragging = true;
        CaptureMouse();
        SetFromX(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetFromX(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndDrag();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
    }

    private void EndDrag()
    {
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void SetFromX(double x)
    {
        double usable = ActualWidth - 2.0 * ThumbRadius;
        if (usable <= 0) return;

        double fraction = Math.Clamp((x - ThumbRadius) / usable, 0.0, 1.0);
        int level = (int)Math.Round(fraction * 100.0, MidpointRounding.AwayFromZero);
        if (level == Value) return;

        Value = level;
        UserChanged?.Invoke(level);
    }

    /// <summary>The thumb keeps its radius inside the control, so it never clips at the ends.</summary>
    private static double ThumbX(int level, double width) =>
        ThumbRadius + (Math.Clamp(level, 0, 100) / 100.0) * Math.Max(0, width - 2.0 * ThumbRadius);

    private static Brush Frozen(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

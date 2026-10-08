using System.Windows;
using System.Windows.Media;
using Island.App.Animations;
using Island.Core.Pomodoro;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace Island.App.Controls;

/// <summary>
/// Small vector phase glyph for the compact Pomodoro presentation.
/// Focus is a target, Break is a steaming cup, and Angry focus uses the target in red.
/// </summary>
public sealed class PomodoroGlyph : FrameworkElement, IFrameClient
{
    private const double DesignSize = 16.0;
    private const double TargetPulseSeconds = 2.2;
    private const double BreakPulseSeconds = 2.8;
    private const double AngryPulseSeconds = 1.1;

    private static readonly Brush FocusBrush = FrozenBrush(0xFF, 0xD6, 0x0A);
    private static readonly Brush BreakBrush = FrozenBrush(0x0A, 0x84, 0xFF);
    private static readonly Brush AngryBrush = FrozenBrush(0xFF, 0x45, 0x3A);
    private static readonly Pen FocusPen = FrozenPen(FocusBrush, 1.35);
    private static readonly Pen BreakPen = FrozenPen(BreakBrush, 1.35);
    private static readonly Pen AngryPen = FrozenPen(AngryBrush, 1.35);
    private static readonly StreamGeometry CupHandle = CreateCupHandle();
    private static readonly StreamGeometry SteamLeft = CreateSteam(6.0);
    private static readonly StreamGeometry SteamRight = CreateSteam(10.0);

    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(PomodoroPhase), typeof(PomodoroGlyph),
        new FrameworkPropertyMetadata(PomodoroPhase.Focus, OnPresentationChanged));

    public static readonly DependencyProperty IsAngryProperty = DependencyProperty.Register(
        nameof(IsAngry), typeof(bool), typeof(PomodoroGlyph),
        new FrameworkPropertyMetadata(false, OnPresentationChanged));

    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.Register(
        nameof(ReduceMotion), typeof(bool), typeof(PomodoroGlyph),
        new FrameworkPropertyMetadata(false, OnPresentationChanged));

    static PomodoroGlyph()
    {
        WidthProperty.OverrideMetadata(typeof(PomodoroGlyph), new FrameworkPropertyMetadata(DesignSize));
        HeightProperty.OverrideMetadata(typeof(PomodoroGlyph), new FrameworkPropertyMetadata(DesignSize));
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(PomodoroGlyph), new FrameworkPropertyMetadata(true));
    }

    private bool _motionRequested;
    private double _elapsed;
    private double _lastSide = -1.0;
    private readonly MatrixTransform _geometryTransform = new();
    private readonly MatrixTransform _motionTransform = new();
    private readonly MatrixTransform _steamLeftTransform = new();
    private readonly MatrixTransform _steamRightTransform = new();

    public PomodoroGlyph()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _motionTransform;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnVisibilityChanged;
    }

    public PomodoroPhase Phase
    {
        get => (PomodoroPhase)GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    /// <summary>Uses the focus target in red while Angry mode is locked.</summary>
    public bool IsAngry
    {
        get => (bool)GetValue(IsAngryProperty);
        set => SetValue(IsAngryProperty, value);
    }

    public bool ReduceMotion
    {
        get => (bool)GetValue(ReduceMotionProperty);
        set => SetValue(ReduceMotionProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double side = Math.Min(ActualWidth, ActualHeight);
        if (side <= 0.0) return;

        double center = side / 2.0;
        double baseScale = side / DesignSize;
        Brush brush = BrushForCurrentState();
        Pen pen = PenForCurrentState();

        if (Math.Abs(_lastSide - side) > 0.001)
        {
            _lastSide = side;
            _geometryTransform.Matrix = new Matrix(
                baseScale, 0.0, 0.0, baseScale, center - 8.0 * baseScale, center - 8.0 * baseScale);
        }

        double motionScale = MotionScale();
        _motionTransform.Matrix = new Matrix(motionScale, 0.0, 0.0, motionScale, 0.0, 0.0);

        drawingContext.PushTransform(_geometryTransform);

        if (Phase == PomodoroPhase.Break)
        {
            DrawBreak(drawingContext, pen);
        }
        else
        {
            DrawFocus(drawingContext, brush, pen);
        }

        drawingContext.Pop();
    }

    bool IFrameClient.OnFrame(double dt) => OnFrame(dt);

    private bool OnFrame(double dt)
    {
        if (!CanAnimate())
        {
            _motionRequested = false;
            _elapsed = 0.0;
            InvalidateVisual();
            return false;
        }

        _elapsed += Math.Max(0.0, dt);
        InvalidateVisual();
        return true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateMotion();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _motionRequested = false;
        _elapsed = 0.0;
        InvalidateVisual();
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateMotion();

    private static void OnPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var glyph = (PomodoroGlyph)d;
        glyph._elapsed = 0.0;
        glyph.UpdateMotion();
        glyph.InvalidateVisual();
    }

    private void UpdateMotion()
    {
        bool shouldAnimate = CanAnimate();
        if (shouldAnimate)
        {
            if (!_motionRequested)
            {
                _motionRequested = true;
                MotionPump.Request(this);
            }
        }
        else
        {
            _motionRequested = false;
            _elapsed = 0.0;
        }

        InvalidateVisual();
    }

    private bool CanAnimate() => IsLoaded && IsVisible && !ReduceMotion && SystemParameters.ClientAreaAnimation;

    private double MotionScale()
    {
        if (!CanAnimate()) return 1.0;

        double period = IsAngry && Phase != PomodoroPhase.Break
            ? AngryPulseSeconds
            : Phase == PomodoroPhase.Break ? BreakPulseSeconds : TargetPulseSeconds;
        double pulse = (1.0 - Math.Cos(_elapsed * Math.PI * 2.0 / period)) * 0.5;
        double amplitude = IsAngry && Phase != PomodoroPhase.Break
            ? 0.10
            : Phase == PomodoroPhase.Break ? 0.04 : 0.07;
        return 1.0 + amplitude * pulse;
    }

    private Brush BrushForCurrentState() => IsAngry && Phase != PomodoroPhase.Break
        ? AngryBrush
        : Phase == PomodoroPhase.Break ? BreakBrush : FocusBrush;

    private Pen PenForCurrentState() => IsAngry && Phase != PomodoroPhase.Break
        ? AngryPen
        : Phase == PomodoroPhase.Break ? BreakPen : FocusPen;

    private static void DrawFocus(DrawingContext dc, Brush brush, Pen pen)
    {
        dc.DrawEllipse(null, pen, new Point(8, 8), 4.35, 4.35);
        dc.DrawLine(pen, new Point(1.5, 8), new Point(4.0, 8));
        dc.DrawLine(pen, new Point(12.0, 8), new Point(14.5, 8));
        dc.DrawLine(pen, new Point(8, 1.5), new Point(8, 4.0));
        dc.DrawLine(pen, new Point(8, 12.0), new Point(8, 14.5));
        dc.DrawEllipse(brush, null, new Point(8, 8), 1.15, 1.15);
    }

    private void DrawBreak(DrawingContext dc, Pen pen)
    {
        dc.DrawRoundedRectangle(null, pen, new Rect(3.0, 7.5, 9.5, 5.0), 1.25, 1.25);
        dc.DrawGeometry(null, pen, CupHandle);

        DrawSteam(dc, pen, SteamLeft, _steamLeftTransform, 0.0);
        DrawSteam(dc, pen, SteamRight, _steamRightTransform, 0.55);
    }

    private void DrawSteam(DrawingContext dc, Pen pen, StreamGeometry steam, MatrixTransform transform, double phaseOffset)
    {
        if (!CanAnimate())
        {
            dc.DrawGeometry(null, pen, steam);
            return;
        }

        const double cycle = 1.6;
        double progress = ((_elapsed / cycle) + phaseOffset) % 1.0;
        double opacity = 0.20 + 0.80 * Math.Sin(Math.PI * progress);
        double offset = -0.70 * progress;
        transform.Matrix = new Matrix(1.0, 0.0, 0.0, 1.0, 0.0, offset);
        dc.PushTransform(transform);
        dc.PushOpacity(opacity);
        dc.DrawGeometry(null, pen, steam);
        dc.Pop();
        dc.Pop();
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }

    private static Brush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static StreamGeometry CreateCupHandle()
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(11.9, 8.8), false, false);
            context.BezierTo(new Point(14.3, 8.5), new Point(14.3, 12.5), new Point(11.9, 12.3), true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static StreamGeometry CreateSteam(double x)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(x, 6.0), false, false);
            context.BezierTo(new Point(x - 1.0, 5.0), new Point(x + 1.0, 4.0), new Point(x, 3.0), true, false);
        }

        geometry.Freeze();
        return geometry;
    }
}

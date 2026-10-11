using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Island.App.Animations;
using Brush = System.Windows.Media.Brush;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Island.App.Views.Widgets;

/// <summary>
/// Progress ring of the countdown: a track and an arc (a Path with an ArcSegment) from twelve o'clock, clockwise.
/// The shown progress follows its target on a critically damped spring, so a jump (a preset, a reset) glides
/// while the frequent updates of a running countdown are set at once.
/// </summary>
public sealed class TimerRing : Canvas, IFrameClient
{
    private const double Diameter = 88;
    private const double StrokeThickness = 5;
    private const double Center = Diameter / 2;
    private const double Radius = (Diameter - StrokeThickness) / 2;
    // A full arc would start and end on one point and draw nothing, so the ring stops just short of closing.
    private const double MaxProgress = 0.9995;
    private const double SettleSeconds = 0.35;
    private const double BreathingSeconds = 1.1;
    private const double BreathingScale = 1.04;

    private readonly Path _arc = new()
    {
        StrokeThickness = StrokeThickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };
    private readonly ScaleTransform _scale = new();
    private readonly SpringValue _progress = new(SpringValue.OmegaForSettleTime(SettleSeconds, 1.0), 1.0, 0.0);

    public TimerRing()
    {
        Width = Diameter;
        Height = Diameter;
        RenderTransform = _scale;
        RenderTransformOrigin = new Point(0.5, 0.5);

        var track = new Ellipse
        {
            Width = Radius * 2,
            Height = Radius * 2,
            StrokeThickness = StrokeThickness,
        };
        track.SetResourceReference(Shape.StrokeProperty, "ProgressTrackBrush");
        SetLeft(track, Center - Radius);
        SetTop(track, Center - Radius);

        _arc.SetResourceReference(Shape.StrokeProperty, "AccentOrangeBrush");
        Children.Add(track);
        Children.Add(_arc);
        Redraw();
    }

    public Brush Tone
    {
        set => _arc.Stroke = value;
    }

    /// <summary>Moves the arc to <paramref name="value"/> (0 to 1). Without <paramref name="animate"/> it jumps.</summary>
    public void SetProgress(double value, bool animate)
    {
        double target = Math.Clamp(value, 0, 1);
        if (!animate)
        {
            _progress.Snap(target);
            Redraw();
            return;
        }

        _progress.SetTarget(target);
        MotionPump.Request(this);
    }

    /// <summary>Scales the whole ring around its centre (the pulse under ten seconds).</summary>
    public void SetScale(double scale)
    {
        _scale.ScaleX = scale;
        _scale.ScaleY = scale;
    }

    /// <summary>The slow "breathing" of a finished countdown. Stop it with <see cref="StopBreathing"/>.</summary>
    public void StartBreathing()
    {
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, Breath());
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, Breath());
    }

    public void StopBreathing()
    {
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        SetScale(1);
    }

    public bool OnFrame(double dt)
    {
        bool settled = _progress.Advance(dt);
        Redraw();
        return !settled;
    }

    private void Redraw()
    {
        double progress = Math.Clamp(_progress.Current, 0, MaxProgress);
        if (progress <= 0)
        {
            _arc.Data = null;
            return;
        }

        double angle = 2 * Math.PI * progress;
        var figure = new PathFigure { StartPoint = new Point(Center, Center - Radius) };
        figure.Segments.Add(new ArcSegment(
            new Point(Center + Radius * Math.Sin(angle), Center - Radius * Math.Cos(angle)),
            new Size(Radius, Radius),
            0,
            progress > 0.5,
            SweepDirection.Clockwise,
            isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        _arc.Data = geometry;
    }

    private static DoubleAnimation Breath() => new(1, BreathingScale, TimeSpan.FromSeconds(BreathingSeconds))
    {
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
    };
}

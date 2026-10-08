using System.Windows;
using System.Windows.Media;
using Island.App.Animations;
using Brush = System.Windows.Media.Brush;

namespace Island.App.Widgets;

/// <summary>
/// Segmented bar for a pomodoro plan: one rounded segment per pomodoro, filled from the left by <see cref="Progress"/>
/// (completed pomodoros plus the fraction of the current one). Changing <see cref="Total"/> morphs the segment count
/// under a spring: the pitch re-lays out continuously, and the last segment grows or fades in (or shrinks out) as the
/// count passes through its fractional value. Orange while a plan runs, red when angry, dim grey when idle.
/// The gap is 3 px, or 2 px when the segments would get narrower than 5 px. Renders only while a spring is moving.
/// </summary>
public sealed class PlanTrack : FrameworkElement, IFrameClient
{
    private const double WideGap = 3.0;
    private const double NarrowGap = 2.0;
    private const double NarrowSegment = 5.0;
    private const double Radius = 2.0;
    private const double MorphSettleSeconds = 0.30;
    private const double MorphZeta = 0.85;
    private const double FillSettleSeconds = 0.28;
    private const double FillZeta = 0.85;

    private static readonly Brush TrackBrush = Frozen(0x3A, 0x3A, 0x3C, 0xFF);
    private static readonly Brush OrangeFill = Frozen(0xFF, 0x9F, 0x0A, 0xFF);
    private static readonly Brush RedFill = Frozen(0xFF, 0x45, 0x3A, 0xFF);
    private static readonly Brush IdleFill = Frozen(0x63, 0x63, 0x66, 0xFF);

    private readonly SpringValue _visualTotal = new(SpringValue.OmegaForSettleTime(MorphSettleSeconds, MorphZeta), MorphZeta, 1.0);
    private readonly SpringValue _visualProgress = new(SpringValue.OmegaForSettleTime(FillSettleSeconds, FillZeta), FillZeta, 0.0);

    private int _total = 1;
    private double _progress;
    private bool _accent;
    private bool _angry;
    private bool _reduceMotion;

    public PlanTrack()
    {
        Height = 4;
    }

    /// <summary>Number of segments (pomodoros). Changing it morphs the bar; before the first layout it snaps.</summary>
    public int Total
    {
        get => _total;
        set
        {
            int total = Math.Max(1, value);
            if (total == _total) return;

            _total = total;
            Settle(_visualTotal, total);
        }
    }

    /// <summary>Filled length in segments, 0..Total: completed pomodoros plus the fraction of the current one.</summary>
    public double Progress
    {
        get => _progress;
        set
        {
            double progress = Math.Max(0.0, value);
            if (progress == _progress) return;

            _progress = progress;
            Settle(_visualProgress, progress);
        }
    }

    /// <summary>Orange fill while a plan runs; dim grey otherwise.</summary>
    public bool Accent
    {
        get => _accent;
        set
        {
            if (value == _accent) return;
            _accent = value;
            InvalidateVisual();
        }
    }

    /// <summary>Red fill instead of orange while the angry session is active.</summary>
    public bool Angry
    {
        get => _angry;
        set
        {
            if (value == _angry) return;
            _angry = value;
            InvalidateVisual();
        }
    }

    /// <summary>When true, changes snap instead of animating.</summary>
    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (value == _reduceMotion) return;
            _reduceMotion = value;
            if (value)
            {
                _visualTotal.Snap(_total);
                _visualProgress.Snap(_progress);
            }
            InvalidateVisual();
        }
    }

    public bool OnFrame(double dt)
    {
        bool totalSettled = _visualTotal.Advance(dt);
        bool progressSettled = _visualProgress.Advance(dt);
        InvalidateVisual();
        return !(totalSettled && progressSettled);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0.0 || height <= 0.0) return;

        double count = Math.Max(1.0, _visualTotal.Current);
        double progress = Math.Clamp(_visualProgress.Current, 0.0, count);
        double gap = (width - WideGap * (count - 1.0)) / count < NarrowSegment ? NarrowGap : WideGap;
        double pitch = (width + gap) / count;
        double segment = pitch - gap;
        double radius = Math.Min(Radius, height / 2.0);
        Brush fill = Angry ? RedFill : Accent ? OrangeFill : IdleFill;

        int segments = (int)Math.Ceiling(count - 1e-9);
        for (int i = 0; i < segments; i++)
        {
            // The last segment is partial while the count is fractional: it shrinks and fades with its share.
            double share = Math.Clamp(count - i, 0.0, 1.0);
            if (share <= 0.0) continue;

            double x = i * pitch;
            double w = segment * share;
            if (share < 1.0) dc.PushOpacity(share);

            dc.DrawRoundedRectangle(TrackBrush, null, new Rect(x, 0.0, w, height), radius, radius);

            double filled = Math.Clamp(progress - i, 0.0, 1.0) * share;
            if (filled > 0.0)
            {
                double fw = segment * filled;
                dc.DrawRoundedRectangle(fill, null, new Rect(x, 0.0, fw, height), Math.Min(radius, fw / 2.0), radius);
            }

            if (share < 1.0) dc.Pop();
        }
    }

    /// <summary>Snaps before the first layout and in reduced motion; otherwise springs toward the value.</summary>
    private void Settle(SpringValue spring, double value)
    {
        if (ReduceMotion || !IsLoaded)
        {
            spring.Snap(value);
        }
        else
        {
            spring.SetTarget(value);
            MotionPump.Request(this);
        }
        InvalidateVisual();
    }

    private static Brush Frozen(byte r, byte g, byte b, byte a)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

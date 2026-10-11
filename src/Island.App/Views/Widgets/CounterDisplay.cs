using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Island.App.Animations;
using Island.Core.Tools.Counter;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;

namespace Island.App.Views.Widgets;

/// <summary>
/// The big number of the counter, drawn under springs. A change of value rolls the digits: the new number comes from
/// below when it grows and from above when it shrinks, while the old one leaves the other way. Each tap pulses the
/// whole control. With a goal it shows "/ goal" and a thin progress bar; reaching the goal tints both green with a
/// short flash. It draws only while a spring is moving.
/// </summary>
public sealed class CounterDisplay : FrameworkElement, IFrameClient
{
    private const double NumberEmSize = 46;
    private const double SuffixEmSize = 11;
    private const double ZoneHeight = 16;
    private const double BarHeight = 3;
    private const double CornerRadius = 14;
    private const double RollDistance = 14;
    private const double RollSeconds = 0.16;
    private const double PulseSeconds = 0.3;
    private const double PulseZeta = 0.55;
    private const double PulseKick = 0.08;
    private const double SettleSeconds = 0.25;
    private const double FlashSeconds = 0.45;
    private const double FlashAlpha = 0.18;

    private static readonly Color TextColor = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color SecondaryColor = Color.FromRgb(0xA1, 0xA1, 0xA6);
    private static readonly Color TrackColor = Color.FromRgb(0x3A, 0x3A, 0x3C);
    private static readonly Color BlueColor = Color.FromRgb(0x0A, 0x84, 0xFF);
    private static readonly Color GreenColor = Color.FromRgb(0x30, 0xD1, 0x58);
    private static readonly SolidColorBrush SecondaryBrush = Solid(SecondaryColor);
    private static readonly SolidColorBrush TrackBrush = Solid(TrackColor);

    private readonly SpringValue _roll = new(SpringValue.OmegaForSettleTime(RollSeconds, 1.0), 1.0, 0.0);
    private readonly SpringValue _pulse = new(SpringValue.OmegaForSettleTime(PulseSeconds, PulseZeta), PulseZeta, 1.0);
    private readonly SpringValue _bar = new(SpringValue.OmegaForSettleTime(SettleSeconds, 1.0), 1.0, 0.0);
    private readonly SpringValue _goalMix = new(SpringValue.OmegaForSettleTime(SettleSeconds, 1.0), 1.0, 0.0);
    private readonly SpringValue _flash = new(SpringValue.OmegaForSettleTime(FlashSeconds, 1.0), 1.0, 0.0);
    private readonly ScaleTransform _scale = new(1.0, 1.0);
    private readonly Typeface _typeface;

    private int _shown;
    private int _previous;
    private int _direction;
    private int _goal;
    private bool _goalReached;
    private bool _reduceMotion;

    public CounterDisplay()
    {
        RenderTransform = _scale;
        RenderTransformOrigin = new Point(0.5, 0.5);
        ToolTip = "Botão direito: definir meta";

        FontFamily family = System.Windows.Application.Current?.TryFindResource("IslandFontFamily") as FontFamily
                            ?? new FontFamily("Segoe UI");
        _typeface = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    }

    /// <summary>Shows the model's number and goal. A changed value rolls, and a newly reached goal flashes.</summary>
    public void Show(CounterModel model, bool reduceMotion) => Apply(model, reduceMotion, animate: true);

    /// <summary>Shows the model's number at once, for when the widget comes back on screen.</summary>
    public void Restore(CounterModel model, bool reduceMotion) => Apply(model, reduceMotion, animate: false);

    private void Apply(CounterModel model, bool reduceMotion, bool animate)
    {
        ArgumentNullException.ThrowIfNull(model);

        int direction = Math.Sign(model.Value - _shown);
        bool reached = model.GoalReached;
        bool flashNow = reached && !_goalReached;
        _goal = model.Goal;
        _goalReached = reached;
        _reduceMotion = reduceMotion;

        if (!animate || !IsLoaded || reduceMotion)
        {
            _roll.Snap(0.0);
            _direction = 0;
            _bar.Snap(model.GoalProgress);
            _goalMix.Snap(reached ? 1.0 : 0.0);
            _flash.Snap(0.0);
        }
        else
        {
            if (direction != 0) StartRoll(direction);
            _bar.SetTarget(model.GoalProgress);
            _goalMix.SetTarget(reached ? 1.0 : 0.0);
            if (flashNow)
            {
                _flash.Snap(1.0);
                _flash.SetTarget(0.0);
            }
            MotionPump.Request(this);
        }

        _shown = model.Value;
        InvalidateVisual();
    }

    /// <summary>Kicks the control up a little and lets the spring bring it back. Ignored when animations are reduced.</summary>
    public void Pulse()
    {
        if (_reduceMotion || !IsLoaded) return;

        _pulse.Snap(1.0 + PulseKick);
        _pulse.SetTarget(1.0);
        MotionPump.Request(this);
    }

    public bool OnFrame(double dt)
    {
        bool rollSettled = _roll.Advance(dt);
        bool pulseSettled = _pulse.Advance(dt);
        bool barSettled = _bar.Advance(dt);
        bool goalSettled = _goalMix.Advance(dt);
        bool flashSettled = _flash.Advance(dt);

        _scale.ScaleX = _pulse.Current;
        _scale.ScaleY = _pulse.Current;
        InvalidateVisual();
        return !(rollSettled && pulseSettled && barSettled && goalSettled && flashSettled);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0.0 || height <= 0.0) return;

        // A transparent surface, so the space around the digits still takes the wheel and the right click.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0.0, 0.0, width, height));

        double mix = Math.Clamp(_goalMix.Current, 0.0, 1.0);
        double zoneHeight = height - ZoneHeight;
        var zone = new Rect(0.0, 0.0, width, zoneHeight);

        double flash = Math.Clamp(_flash.Current, 0.0, 1.0);
        if (flash > 0.0)
        {
            byte alpha = (byte)Math.Round(255.0 * FlashAlpha * flash);
            dc.DrawRoundedRectangle(Solid(Color.FromArgb(alpha, GreenColor.R, GreenColor.G, GreenColor.B)), null,
                zone, CornerRadius, CornerRadius);
        }

        DrawNumbers(dc, zone, Solid(Lerp(TextColor, GreenColor, mix)));

        if (_goal > 0) DrawGoal(dc, width, height, mix);
    }

    private void StartRoll(int direction)
    {
        // A flip of direction, or a roll that already settled, starts from the top. A retarget in the same direction
        // keeps its position and velocity, so fast taps roll on without a jump.
        if (direction != _direction || _roll.IsSettled) _roll.Snap(1.0);
        _roll.SetTarget(0.0);
        _previous = _shown;
        _direction = direction;
        MotionPump.Request(this);
    }

    private void DrawNumbers(DrawingContext dc, Rect zone, Brush brush)
    {
        double center = zone.Width / 2.0;
        double roll = Math.Clamp(_roll.Current, 0.0, 1.0);

        dc.PushClip(new RectangleGeometry(zone));
        if (roll > 0.0 && _direction != 0)
        {
            // The outgoing number leaves the way the new one came from.
            DrawNumber(dc, _previous, brush, center, zone.Height, -_direction * RollDistance * (1.0 - roll), roll);
        }
        DrawNumber(dc, _shown, brush, center, zone.Height, _direction * RollDistance * roll, 1.0 - roll);
        dc.Pop();
    }

    private void DrawNumber(DrawingContext dc, int value, Brush brush, double center, double zoneHeight,
        double offsetY, double alpha)
    {
        if (alpha <= 0.0) return;

        FormattedText text = BuildText(CounterFormat.ValueText(value), brush, NumberEmSize);
        var origin = new Point(center - text.Width / 2.0, (zoneHeight - text.Height) / 2.0 + offsetY);
        if (alpha >= 1.0)
        {
            dc.DrawText(text, origin);
            return;
        }

        dc.PushOpacity(alpha);
        dc.DrawText(text, origin);
        dc.Pop();
    }

    private void DrawGoal(DrawingContext dc, double width, double height, double mix)
    {
        FormattedText suffix = BuildText(CounterFormat.GoalSuffix(_goal), SecondaryBrush, SuffixEmSize);
        dc.DrawText(suffix, new Point(width - suffix.Width, height - BarHeight - 4.0 - suffix.Height));

        double radius = BarHeight / 2.0;
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0.0, height - BarHeight, width, BarHeight), radius, radius);

        double fill = width * Math.Clamp(_bar.Current, 0.0, 1.0);
        if (fill <= 0.0) return;

        Brush fillBrush = Solid(Lerp(BlueColor, GreenColor, mix));
        dc.DrawRoundedRectangle(fillBrush, null, new Rect(0.0, height - BarHeight, fill, BarHeight), radius, radius);
    }

    private FormattedText BuildText(string text, Brush brush, double emSize) => new(
        text,
        CultureInfo.CurrentCulture,
        System.Windows.FlowDirection.LeftToRight,
        _typeface,
        emSize,
        brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Color Lerp(Color from, Color to, double t) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t),
        (byte)Math.Round(from.B + (to.B - from.B) * t));

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

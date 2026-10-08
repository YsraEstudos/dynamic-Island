using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Island.App.Animations;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;

namespace Island.App.Widgets;

/// <summary>
/// Pomodoro count stepper: [−] [number] [+] on a rounded chip (76 x 26). The number rolls vertically when
/// <see cref="Value"/> changes: the new number comes from below when it grows and from above when it shrinks, while
/// the old one leaves the other way, both under a damped spring and clipped to the number zone.
/// The − and + zones raise <see cref="Stepped"/> with +1 or -1 on press, repeat while held (after 380 ms, the interval
/// starts at 140 ms and shortens to 45 ms), and the mouse wheel steps too. The glyphs are round-capped strokes that
/// spring slightly on press. The control renders only while a spring is moving.
/// </summary>
public sealed class CycleStepper : FrameworkElement, IFrameClient
{
    private const double ChipRadius = 13.0;
    private const double ZoneWidth = 26.0;
    private const double GlyphHalf = 4.0;
    private const double GlyphStroke = 1.8;
    private const double DimOpacity = 0.35;
    private const double NumberEmSize = 13.0;
    private const double RollDistance = 12.0;
    private const double RollSettleSeconds = 0.28;
    private const double RollZeta = 0.8;
    private const double PressedScale = 0.85;
    private const double PressSettleSeconds = 0.18;
    private const double PressZeta = 0.85;
    private const double RepeatDelayMs = 380.0;
    private const double RepeatStartMs = 140.0;
    private const double RepeatMinMs = 45.0;
    private const double RepeatDecay = 0.85;
    private const int TextCacheSize = 64;

    private static readonly Brush ChipBrush = Frozen(0x2C, 0x2C, 0x2E, 0xFF);
    private static readonly Brush NumberBrush = Frozen(0xF2, 0xF2, 0xF7, 0xFF);
    private static readonly Brush OrangeBrush = Frozen(0xFF, 0x9F, 0x0A, 0xFF);
    private static readonly Color GreyColor = Color.FromRgb(0xA1, 0xA1, 0xA6);
    private static readonly Color OrangeColor = Color.FromRgb(0xFF, 0x9F, 0x0A);
    private static readonly Pen GreyPen = MakeGlyphPen(GreyColor, 1.0);
    private static readonly Pen GreyDimPen = MakeGlyphPen(GreyColor, DimOpacity);
    private static readonly Pen OrangePen = MakeGlyphPen(OrangeColor, 1.0);
    private static readonly Pen OrangeDimPen = MakeGlyphPen(OrangeColor, DimOpacity);

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(CycleStepper), new PropertyMetadata(1, OnValueChanged));

    private readonly SpringValue _roll = new(SpringValue.OmegaForSettleTime(RollSettleSeconds, RollZeta), RollZeta, 0.0);
    private readonly SpringValue _minusPress = new(SpringValue.OmegaForSettleTime(PressSettleSeconds, PressZeta), PressZeta, 1.0);
    private readonly SpringValue _plusPress = new(SpringValue.OmegaForSettleTime(PressSettleSeconds, PressZeta), PressZeta, 1.0);
    private readonly RectangleGeometry _numberClip = new();
    private readonly Typeface _typeface;
    private readonly FormattedText?[] _texts = new FormattedText?[TextCacheSize];

    private int _previous;
    private int _direction;
    private int _held;
    private double _cachedDpi;
    private double _nextRepeatMs;
    private DispatcherTimer? _repeatTimer;
    private bool _canDecrease = true;
    private bool _canIncrease = true;
    private bool _accent;
    private bool _reduceMotion;

    public CycleStepper()
    {
        Width = 76;
        Height = 26;
        Cursor = Cursors.Hand;

        FontFamily family = System.Windows.Application.Current?.TryFindResource("IslandFontFamily") as FontFamily
                            ?? new FontFamily("Segoe UI");
        _typeface = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        MouseLeftButtonDown += OnDown;
        MouseLeftButtonUp += OnUp;
        MouseLeave += (_, _) => Release();
        LostMouseCapture += (_, _) => Release();
        MouseWheel += OnWheel;
    }

    /// <summary>Raised with +1 or -1 when a step is requested (press, held repeat or wheel). Not raised when the step is blocked.</summary>
    public event Action<int>? Stepped;

    /// <summary>The number shown in the middle.</summary>
    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>False dims the − glyph and ignores it.</summary>
    public bool CanDecrease
    {
        get => _canDecrease;
        set
        {
            if (value == _canDecrease) return;
            _canDecrease = value;
            InvalidateVisual();
        }
    }

    /// <summary>False dims the + glyph and ignores it.</summary>
    public bool CanIncrease
    {
        get => _canIncrease;
        set
        {
            if (value == _canIncrease) return;
            _canIncrease = value;
            InvalidateVisual();
        }
    }

    /// <summary>Orange glyphs and number while a plan runs; otherwise grey glyphs and a white number.</summary>
    public bool Accent
    {
        get => _accent;
        set
        {
            if (value == _accent) return;
            _accent = value;
            Array.Clear(_texts);
            InvalidateVisual();
        }
    }

    /// <summary>When true, value changes and presses snap instead of animating.</summary>
    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (value == _reduceMotion) return;
            _reduceMotion = value;
            if (value)
            {
                _roll.Snap(0.0);
                _minusPress.Snap(1.0);
                _plusPress.Snap(1.0);
                _direction = 0;
            }
            InvalidateVisual();
        }
    }

    public bool OnFrame(double dt)
    {
        bool rollSettled = _roll.Advance(dt);
        bool minusSettled = _minusPress.Advance(dt);
        bool plusSettled = _plusPress.Advance(dt);
        InvalidateVisual();
        return !(rollSettled && minusSettled && plusSettled);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0.0 || height <= 0.0) return;

        var bounds = new Rect(0.0, 0.0, width, height);
        // Transparent full-size surface so the empty corners of the chip still take the pointer.
        dc.DrawRectangle(Brushes.Transparent, null, bounds);
        double radius = Math.Min(ChipRadius, height / 2.0);
        dc.DrawRoundedRectangle(ChipBrush, null, bounds, radius, radius);

        double centerY = height / 2.0;
        double minusX = ZoneWidth / 2.0;
        double plusX = width - ZoneWidth / 2.0;
        DrawGlyph(dc, MinusPen(), minusX, centerY, GlyphHalf * _minusPress.Current, plus: false);
        DrawGlyph(dc, PlusPen(), plusX, centerY, GlyphHalf * _plusPress.Current, plus: true);

        // The number rolls inside the middle zone only.
        double left = ZoneWidth;
        double right = width - ZoneWidth;
        _numberClip.Rect = new Rect(left, 0.0, right - left, height);
        double center = (left + right) / 2.0;
        double roll = _roll.Current;

        dc.PushClip(_numberClip);
        if (roll > 0.0 && _direction != 0)
        {
            // The outgoing number moves away the way the new one came from.
            DrawNumber(dc, _previous, center, height, -_direction * RollDistance * (1.0 - roll), roll);
        }
        DrawNumber(dc, Value, center, height, _direction * RollDistance * roll, 1.0 - roll);
        dc.Pop();
    }

    private void DrawNumber(DrawingContext dc, int value, double center, double height, double offsetY, double alpha)
    {
        if (alpha <= 0.0) return;

        FormattedText text = TextFor(value);
        var origin = new Point(center - text.Width / 2.0, (height - text.Height) / 2.0 + offsetY);
        if (alpha >= 1.0)
        {
            dc.DrawText(text, origin);
            return;
        }

        dc.PushOpacity(alpha);
        dc.DrawText(text, origin);
        dc.Pop();
    }

    private static void DrawGlyph(DrawingContext dc, Pen pen, double cx, double cy, double half, bool plus)
    {
        dc.DrawLine(pen, new Point(cx - half, cy), new Point(cx + half, cy));
        if (plus) dc.DrawLine(pen, new Point(cx, cy - half), new Point(cx, cy + half));
    }

    private Pen MinusPen() => Accent
        ? (CanDecrease ? OrangePen : OrangeDimPen)
        : (CanDecrease ? GreyPen : GreyDimPen);

    private Pen PlusPen() => Accent
        ? (CanIncrease ? OrangePen : OrangeDimPen)
        : (CanIncrease ? GreyPen : GreyDimPen);

    /// <summary>Cached per value and per pixel density. Values outside the cache are built each time.</summary>
    private FormattedText TextFor(int value)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (dpi != _cachedDpi)
        {
            Array.Clear(_texts);
            _cachedDpi = dpi;
        }

        if ((uint)value >= (uint)_texts.Length) return BuildText(value, dpi);
        return _texts[value] ??= BuildText(value, dpi);
    }

    private FormattedText BuildText(int value, double dpi) => new(
        value.ToString(CultureInfo.InvariantCulture),
        CultureInfo.CurrentCulture,
        System.Windows.FlowDirection.LeftToRight,
        _typeface,
        NumberEmSize,
        Accent ? OrangeBrush : NumberBrush,
        dpi);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((CycleStepper)d).OnValueChanged((int)e.OldValue, (int)e.NewValue);
    }

    private void OnValueChanged(int from, int to)
    {
        int direction = Math.Sign(to - from);
        if (direction == 0) return;

        if (!IsLoaded || ReduceMotion)
        {
            _roll.Snap(0.0);
            _direction = 0;
        }
        else
        {
            // A new roll starts from the top when the direction flips or the spring is at rest. Mid-flight retargets
            // in the same direction keep their position and velocity, so fast repeats roll on smoothly.
            _previous = from;
            if (direction != _direction || _roll.Current == _roll.Target) _roll.Snap(1.0);
            _direction = direction;
            _roll.SetTarget(0.0);
            MotionPump.Request(this);
        }
        InvalidateVisual();
    }

    private int ZoneAt(double x)
    {
        if (x < ZoneWidth) return -1;
        if (x > ActualWidth - ZoneWidth) return 1;
        return 0;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        int delta = ZoneAt(e.GetPosition(this).X);
        if (delta == 0) return;

        _held = delta;
        CaptureMouse();
        SetPressed(delta, pressed: true);
        e.Handled = true;

        Step(delta);
        StartRepeat();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_held == 0) return;

        Release();
        e.Handled = true;
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        Step(e.Delta > 0 ? 1 : -1);
    }

    /// <summary>Ends the press: stops the repeat, releases the capture and springs the glyph back.</summary>
    private void Release()
    {
        StopRepeat();
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (_held == 0) return;

        SetPressed(_held, pressed: false);
        _held = 0;
    }

    private void SetPressed(int delta, bool pressed)
    {
        SpringValue press = delta < 0 ? _minusPress : _plusPress;
        double target = pressed ? PressedScale : 1.0;
        if (ReduceMotion)
        {
            press.Snap(target);
        }
        else
        {
            press.SetTarget(target);
            MotionPump.Request(this);
        }
        InvalidateVisual();
    }

    /// <summary>Raises <see cref="Stepped"/> when the direction can still move. Returns false when blocked.</summary>
    private bool Step(int delta)
    {
        if (delta > 0 ? !CanIncrease : !CanDecrease) return false;

        Stepped?.Invoke(delta);
        return true;
    }

    private void StartRepeat()
    {
        _repeatTimer ??= CreateRepeatTimer();
        _nextRepeatMs = RepeatStartMs;
        _repeatTimer.Interval = TimeSpan.FromMilliseconds(RepeatDelayMs);
        _repeatTimer.Start();
    }

    private void StopRepeat() => _repeatTimer?.Stop();

    private DispatcherTimer CreateRepeatTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += OnRepeatTick;
        return timer;
    }

    private void OnRepeatTick(object? sender, EventArgs e)
    {
        if (_held == 0 || !Step(_held))
        {
            StopRepeat();
            return;
        }

        // Each repeat is a little quicker than the last, down to the minimum interval.
        _repeatTimer!.Interval = TimeSpan.FromMilliseconds(_nextRepeatMs);
        _nextRepeatMs = Math.Max(RepeatMinMs, _nextRepeatMs * RepeatDecay);
    }

    private static Pen MakeGlyphPen(Color color, double alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255.0), color.R, color.G, color.B));
        brush.Freeze();
        var pen = new Pen(brush, GlyphStroke)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        return pen;
    }

    private static Brush Frozen(byte r, byte g, byte b, byte a)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

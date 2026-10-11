using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Island.App.Animations;
using Island.Core.Tools.FocusSounds;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Island.App.Views.Widgets;

/// <summary>
/// One sound on the Sons de foco widget: an icon and a short name, with a thin level bar under them. Switching it on
/// springs the colours over and gives the chip a small pulse; the mouse wheel reports steps so the widget can change its
/// level. The chip only shows state the widget hands it; the model stays the source of truth.
/// </summary>
public sealed class FocusSoundChip : Border, IFrameClient
{
    private const double PressedScale = 0.92;
    private const double PulseScale = 1.06;

    private static readonly Color OffFill = Color.FromRgb(0x24, 0x24, 0x26);
    private static readonly Color OnFill = Color.FromRgb(0x1B, 0x3A, 0x4A);
    private static readonly Color OffText = Color.FromRgb(0xA1, 0xA1, 0xA6);
    private static readonly Color OnText = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color OffIcon = Color.FromRgb(0x6E, 0x6E, 0x73);
    private static readonly Color OnIcon = Color.FromRgb(0x64, 0xD2, 0xFF);
    private static readonly Brush LevelTrackBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C));
    private static readonly Brush LevelFillBrush = new SolidColorBrush(OnIcon);

    private readonly SolidColorBrush _fillBrush = new(OffFill);
    private readonly SolidColorBrush _textBrush = new(OffText);
    private readonly SolidColorBrush _iconBrush = new(OffIcon);
    private readonly ScaleTransform _scale = new(1.0, 1.0);
    private readonly SpringValue _pressSpring = new(SpringValue.OmegaForSettleTime(0.2, 0.75), 0.75, 1.0);
    private readonly SpringValue _onSpring = new(SpringValue.OmegaForSettleTime(0.22, 1.0), 1.0, 0.0);
    private readonly Grid _levelBar;
    private readonly Border _levelFill;
    private bool _pressed;
    private bool _rendered;
    private bool _on;
    private bool _reduceMotion;
    private double _level;

    public FocusSoundChip(FocusSoundKind kind, string name, Geometry glyph)
    {
        Kind = kind;
        CornerRadius = new CornerRadius(10);
        Background = _fillBrush;
        Cursor = Cursors.Hand;
        RenderTransform = _scale;
        RenderTransformOrigin = new Point(0.5, 0.5);
        AutomationProperties.SetName(this, $"Som {name}");

        var icon = new Canvas { Width = 24, Height = 24 };
        icon.Children.Add(new Path
        {
            Data = glyph,
            Stroke = _iconBrush,
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 3),
        };
        content.Children.Add(new Viewbox { Width = 12, Height = 12, Stretch = Stretch.Uniform, Child = icon });
        content.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = _textBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.SetResourceReference(TextBlock.FontFamilyProperty, "IslandFontFamily");

        var levelTrack = new Border
        {
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = LevelTrackBrush,
        };
        _levelFill = new Border
        {
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = LevelFillBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _levelBar = new Grid
        {
            Margin = new Thickness(10, 0, 10, 4),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        _levelBar.Children.Add(levelTrack);
        _levelBar.Children.Add(_levelFill);

        var root = new Grid();
        root.Children.Add(content);
        root.Children.Add(_levelBar);
        Child = root;

        SizeChanged += (_, _) => ApplyLevel();
        MouseLeftButtonDown += OnPressed;
        MouseLeftButtonUp += OnReleased;
        MouseWheel += OnWheel;
    }

    public FocusSoundKind Kind { get; }

    /// <summary>Raised when the chip is pressed and released over itself.</summary>
    public event Action? Clicked;

    /// <summary>Raised with +1 or -1 for each wheel notch over the chip.</summary>
    public event Action<int>? Wheeled;

    /// <summary>Shows the state the widget hands in. Only a change of <paramref name="on"/> animates; the level bar just follows.</summary>
    public void Render(bool on, double level, bool reduceMotion)
    {
        _level = Math.Clamp(level, 0.0, 1.0);
        _reduceMotion = reduceMotion;
        ApplyLevel();

        if (_rendered && on == _on) return;

        // The first render (the widget being shown again) and reduced motion jump straight to the state.
        bool animate = _rendered && !reduceMotion;
        _rendered = true;
        _on = on;
        if (!animate)
        {
            _onSpring.Snap(on ? 1.0 : 0.0);
            ApplyColors();
            return;
        }

        _onSpring.SetTarget(on ? 1.0 : 0.0);
        if (on)
        {
            // A short overshoot on enable: the chip starts larger and settles back.
            _pressSpring.Snap(PulseScale);
            SetPressTarget(CurrentPressTarget);
        }
        MotionPump.Request(this);
    }

    public bool OnFrame(double dt)
    {
        bool colourSettled = _onSpring.Advance(dt);
        bool scaleSettled = _pressSpring.Advance(dt);
        ApplyColors();
        ApplyScale();
        return !(colourSettled && scaleSettled);
    }

    private double CurrentPressTarget => _pressed ? PressedScale : 1.0;

    private void OnPressed(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        CaptureMouse();
        SetPressTarget(PressedScale);
        e.Handled = true;
    }

    private void OnReleased(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;

        _pressed = false;
        ReleaseMouseCapture();
        SetPressTarget(1.0);
        e.Handled = true;

        Point position = e.GetPosition(this);
        bool inside = position.X >= 0.0 && position.Y >= 0.0 && position.X <= ActualWidth && position.Y <= ActualHeight;
        if (inside) Clicked?.Invoke();
    }

    /// <summary>Springs the press scale toward <paramref name="target"/>, or jumps there when reduced motion is on.</summary>
    private void SetPressTarget(double target)
    {
        if (_reduceMotion)
        {
            _pressSpring.Snap(target);
            ApplyScale();
            return;
        }

        _pressSpring.SetTarget(target);
        MotionPump.Request(this);
    }

    private void ApplyScale()
    {
        _scale.ScaleX = _pressSpring.Current;
        _scale.ScaleY = _pressSpring.Current;
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        Wheeled?.Invoke(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    private void ApplyLevel() => _levelFill.Width = Math.Max(0.0, _levelBar.ActualWidth * _level);

    private void ApplyColors()
    {
        double t = Math.Clamp(_onSpring.Current, 0.0, 1.0);
        _fillBrush.Color = Lerp(OffFill, OnFill, t);
        _textBrush.Color = Lerp(OffText, OnText, t);
        _iconBrush.Color = Lerp(OffIcon, OnIcon, t);
    }

    private static Color Lerp(Color from, Color to, double t) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t),
        (byte)Math.Round(from.B + (to.B - from.B) * t));
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Island.App.Animations;
using Island.Core.Performance;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Grid = System.Windows.Controls.Grid;
using Point = System.Windows.Point;

namespace Island.App.Views.Widgets;

/// <summary>
/// One metric of the performance widget: name and temperature on top, the value with a detail (RAM, VRAM), a thin bar
/// that springs to each new value, and a sparkline of the last minute. The bar takes frames only while it moves.
/// Colours follow <see cref="MetricLevel"/>: neutral, then amber, then red as the value nears its limit.
/// </summary>
public sealed class PerformanceMetricPanel : Grid, IFrameClient
{
    private static readonly Brush NeutralBrush = Frozen(0x8E, 0x8E, 0x93);
    private static readonly Brush WarningBrush = Frozen(0xFF, 0x9F, 0x0A);
    private static readonly Brush DangerBrush = Frozen(0xFF, 0x45, 0x3A);
    private static readonly Brush TrackBrush = Frozen(0x2C, 0x2C, 0x2E);
    private static readonly Brush TextPrimary = Frozen(0xFF, 0xFF, 0xFF);
    private static readonly Brush TextSecondary = Frozen(0xA1, 0xA1, 0xA6);
    private static readonly Brush TextTertiary = Frozen(0x6E, 0x6E, 0x73);

    /// <summary>Settle time of the bar, in seconds. Short, so the bar follows the value without lagging behind it.</summary>
    private const double BarSettleSeconds = 0.45;

    private readonly TextBlock _name = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = TextSecondary };
    private readonly TextBlock _temp = new() { FontSize = 11, Foreground = TextTertiary, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
    private readonly TextBlock _value = new()
    {
        FontSize = 22,
        FontWeight = FontWeights.SemiBold,
        Foreground = TextPrimary,
        VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
    };
    private readonly TextBlock _detail = new()
    {
        FontSize = 10,
        Foreground = TextSecondary,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Border _fill = new() { CornerRadius = new CornerRadius(2), Background = NeutralBrush, HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch };
    private readonly ScaleTransform _scale = new(0.0, 1.0);
    private readonly Canvas _sparkCanvas = new() { ClipToBounds = true };
    private readonly Path _spark = new() { StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly SpringValue _bar = new(SpringValue.OmegaForSettleTime(BarSettleSeconds, 0.8), 0.8, 0.0);

    private double?[] _history = Array.Empty<double?>();
    private MetricLevel _level = MetricLevel.Neutral;

    public PerformanceMetricPanel()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });

        var header = new Grid();
        header.Children.Add(_name);
        header.Children.Add(_temp);
        SetRow(header, 0);
        Children.Add(header);

        var valueRow = new Grid();
        valueRow.Children.Add(_value);
        valueRow.Children.Add(_detail);
        SetRow(valueRow, 1);
        Children.Add(valueRow);

        var track = new Border
        {
            Background = TrackBrush,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Height = 4,
            Child = _fill,
        };
        _fill.Height = 4;
        _fill.RenderTransformOrigin = new Point(0.0, 0.5);
        _fill.RenderTransform = _scale;
        SetRow(track, 2);
        Children.Add(track);

        _sparkCanvas.Children.Add(_spark);
        _sparkCanvas.SizeChanged += (_, _) => DrawSpark();
        SetRow(_sparkCanvas, 3);
        Children.Add(_sparkCanvas);
    }

    /// <summary>Metric name, such as "CPU".</summary>
    public string Title
    {
        get => _name.Text;
        set => _name.Text = value;
    }

    /// <summary>Shows a percentage. Unknown shows a dash and an empty bar. The bar springs to the new value.</summary>
    public void SetPercent(double? percent, bool reduceMotion)
    {
        _value.Text = percent is { } p ? $"{p:0}%" : "—";
        ApplyLevel(PerformanceValues.PercentLevel(percent));
        SetBarTarget(PerformanceValues.Fraction(percent), reduceMotion);
    }

    /// <summary>Shows a temperature coloured against its limit. <paramref name="unavailableTip"/> explains a missing reading.</summary>
    public void SetTemperature(double? celsius, int limitC, string? unavailableTip)
    {
        _temp.Text = celsius is { } c ? $"{c:0} °C" : "—";
        _temp.Foreground = BrushFor(PerformanceValues.TemperatureLevel(celsius, limitC));
        _temp.ToolTip = celsius is null ? unavailableTip : null;
    }

    /// <summary>Secondary line next to the value: RAM for the CPU, VRAM for the GPU.</summary>
    public void SetDetail(string? text) => _detail.Text = text ?? string.Empty;

    /// <summary>Redraws the sparkline. Called once per sample, not per frame.</summary>
    public void SetHistory(double?[] history)
    {
        _history = history;
        DrawSpark();
    }

    bool IFrameClient.OnFrame(double dt)
    {
        bool settled = _bar.Advance(dt);
        _scale.ScaleX = Math.Clamp(_bar.Current, 0.0, 1.0);
        return !settled;
    }

    private void SetBarTarget(double fraction, bool reduceMotion)
    {
        if (reduceMotion)
        {
            _bar.Snap(fraction);
            _scale.ScaleX = fraction;
            return;
        }

        _bar.SetTarget(fraction);
        MotionPump.Request(this);
    }

    private void ApplyLevel(MetricLevel level)
    {
        if (level == _level) return;
        _level = level;

        Brush brush = BrushFor(level);
        _value.Foreground = level == MetricLevel.Neutral ? TextPrimary : brush;
        _fill.Background = brush;
        _spark.Stroke = brush;
    }

    private void DrawSpark()
    {
        double width = _sparkCanvas.ActualWidth;
        double height = _sparkCanvas.ActualHeight;
        if (width <= 0 || height <= 0 || _history.Length == 0)
        {
            _spark.Data = null;
            return;
        }

        // Newest sample at the right edge; the line breaks at unknown readings.
        int capacity = Math.Max(PerformanceHistory.Capacity, _history.Length);
        int offset = capacity - _history.Length;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            bool open = false;
            for (int i = 0; i < _history.Length; i++)
            {
                if (_history[i] is not { } value)
                {
                    open = false;
                    continue;
                }

                var point = new Point((offset + i) / (capacity - 1.0) * width, height - Math.Clamp(value / 100.0, 0.0, 1.0) * height);
                if (open) context.LineTo(point, isStroked: true, isSmoothJoin: false);
                else context.BeginFigure(point, isFilled: false, isClosed: false);
                open = true;
            }
        }

        geometry.Freeze();
        _spark.Data = geometry;
    }

    private static Brush BrushFor(MetricLevel level) => level switch
    {
        MetricLevel.Warning => WarningBrush,
        MetricLevel.Danger => DangerBrush,
        _ => NeutralBrush,
    };

    private static Brush Frozen(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

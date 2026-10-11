using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Island.App.Animations;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Island.App.Views.Widgets;

/// <summary>
/// One tile of the Analisador grid: a value over a small caption. A changed value counts from the one on screen,
/// and the tile can fade and slide in on its own delay.
/// </summary>
public sealed class TextAnalyzerCard : Border
{
    private const double SlideDistance = 6;
    private const int RevealMilliseconds = 220;

    private static readonly SolidColorBrush CardFill = Frozen(Color.FromRgb(0x20, 0x20, 0x22));

    private readonly TextBlock _value;
    private readonly TextBlock _label;
    private readonly TranslateTransform _slide = new();
    private readonly CountTween _tween;
    private Func<double, string> _format = _ => string.Empty;
    private double _current;
    private bool _hasValue;

    public TextAnalyzerCard()
    {
        Background = CardFill;
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(6, 0, 6, 0);
        RenderTransform = _slide;

        _value = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _value.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        _label = new TextBlock
        {
            FontSize = 9.5,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _label.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");

        Child = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _value, _label },
        };

        _tween = new CountTween(value =>
        {
            _current = value;
            _value.Text = _format(value);
        });
    }

    /// <summary>Small caption under the value.</summary>
    public string Label
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    /// <summary>
    /// Shows <paramref name="target"/>. With <paramref name="animate"/> and a value already on screen, the number
    /// counts from that value; otherwise it is set at once.
    /// </summary>
    public void Show(double target, Func<double, string> format, bool animate)
    {
        _format = format;
        if (animate && _hasValue)
        {
            _tween.Start(_current, target);
        }
        else
        {
            _tween.Stop();
            _current = target;
            _value.Text = format(target);
        }
        _hasValue = true;
    }

    /// <summary>Fades and slides the tile in after <paramref name="delay"/>. Without animation it is shown at once.</summary>
    public void Reveal(bool animate, TimeSpan delay)
    {
        BeginAnimation(OpacityProperty, null);
        _slide.BeginAnimation(TranslateTransform.YProperty, null);

        if (!animate)
        {
            Opacity = 1;
            _slide.Y = 0;
            return;
        }

        // The base values sit at the start of the fade, so the tile stays hidden through its delay.
        Opacity = 0;
        _slide.Y = SlideDistance;
        BeginAnimation(OpacityProperty, Ease(0, 1, delay));
        _slide.BeginAnimation(TranslateTransform.YProperty, Ease(SlideDistance, 0, delay));
    }

    private static DoubleAnimation Ease(double from, double to, TimeSpan delay) => new(
        from,
        to,
        TimeSpan.FromMilliseconds(RevealMilliseconds))
    {
        BeginTime = delay,
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
    };

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

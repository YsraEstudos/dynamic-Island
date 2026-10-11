using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island.App.Views.Widgets;

/// <summary>
/// Motion of the calculator number: a short slide and fade when a result arrives, a horizontal shake on error and a
/// smooth font shrink as the number grows. Animations end on their own, so an idle display runs nothing.
/// The text block must carry a TranslateTransform as its RenderTransform.
/// </summary>
public sealed class CalculatorDisplayMotion(TextBlock text)
{
    private const int SlideMilliseconds = 140;
    private const int ShakeMilliseconds = 220;
    private const int FontMilliseconds = 140;
    private const double SlideDistance = 6.0;
    private const double ShakeDistance = 5.0;

    private readonly TranslateTransform _offset = (TranslateTransform)text.RenderTransform;

    /// <summary>Slides the new result up into place while it fades in.</summary>
    public void Slide()
    {
        Reset();
        _offset.BeginAnimation(TranslateTransform.YProperty, Ease(SlideDistance, 0.0, SlideMilliseconds));
        text.BeginAnimation(UIElement.OpacityProperty, Ease(0.0, 1.0, SlideMilliseconds));
    }

    /// <summary>Three swings left and right, then back to rest.</summary>
    public void Shake()
    {
        Reset();
        var swing = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ShakeMilliseconds) };
        swing.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)));
        swing.KeyFrames.Add(new LinearDoubleKeyFrame(-ShakeDistance, KeyTime.FromPercent(0.25)));
        swing.KeyFrames.Add(new LinearDoubleKeyFrame(ShakeDistance, KeyTime.FromPercent(0.5)));
        swing.KeyFrames.Add(new LinearDoubleKeyFrame(-ShakeDistance, KeyTime.FromPercent(0.75)));
        swing.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
        _offset.BeginAnimation(TranslateTransform.XProperty, swing);
    }

    /// <summary>Sets the number's font size, easing there when <paramref name="animate"/> is set.</summary>
    public void SetFontSize(double size, bool animate)
    {
        // Clearing first matters: a finished animation holds its value and would hide a direct change.
        text.BeginAnimation(TextBlock.FontSizeProperty, null);
        if (!animate)
        {
            text.FontSize = size;
            return;
        }

        text.BeginAnimation(TextBlock.FontSizeProperty, Ease(null, size, FontMilliseconds));
    }

    /// <summary>Stops any slide or shake and puts the number back in place.</summary>
    public void Reset()
    {
        _offset.BeginAnimation(TranslateTransform.XProperty, null);
        _offset.BeginAnimation(TranslateTransform.YProperty, null);
        text.BeginAnimation(UIElement.OpacityProperty, null);
        _offset.X = 0.0;
        _offset.Y = 0.0;
        text.Opacity = 1.0;
    }

    private static DoubleAnimation Ease(double from, double to, int milliseconds) =>
        Ease((double?)from, to, milliseconds);

    private static DoubleAnimation Ease(double? from, double to, int milliseconds) => new()
    {
        From = from,
        To = to,
        Duration = TimeSpan.FromMilliseconds(milliseconds),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
    };
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island.App.Budgets.Views;

/// <summary>
/// Attached property "Ratio" (0..1). Ao mudar, anima ScaleTransform.ScaleX do elemento
/// (que deve ter RenderTransform = ScaleTransform). Só anima Transform, nunca o layout.
/// </summary>
public static class ProgressBarAnimator
{
    private static readonly Duration FillDuration = new(TimeSpan.FromMilliseconds(350));

    public static readonly DependencyProperty RatioProperty = DependencyProperty.RegisterAttached(
        "Ratio",
        typeof(double),
        typeof(ProgressBarAnimator),
        new PropertyMetadata(0d, OnRatioChanged));

    public static double GetRatio(DependencyObject element) => (double)element.GetValue(RatioProperty);

    public static void SetRatio(DependencyObject element, double value) => element.SetValue(RatioProperty, value);

    private static void OnRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement { RenderTransform: ScaleTransform scale })
        {
            return;
        }

        double target = Math.Clamp((double)e.NewValue, 0, 1);
        var animation = new DoubleAnimation(target, FillDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
    }
}

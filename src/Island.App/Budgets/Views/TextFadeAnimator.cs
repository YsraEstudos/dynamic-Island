using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island.App.Budgets.Views;

/// <summary>
/// Attached property "FadeText" para TextBlock: define o Text e faz um fade curto (180 ms)
/// quando o valor muda. Não usa timer nem animação contínua.
/// </summary>
public static class TextFadeAnimator
{
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(180));

    public static readonly DependencyProperty FadeTextProperty = DependencyProperty.RegisterAttached(
        "FadeText",
        typeof(string),
        typeof(TextFadeAnimator),
        new PropertyMetadata(string.Empty, OnFadeTextChanged));

    public static string GetFadeText(DependencyObject element) => (string)element.GetValue(FadeTextProperty);

    public static void SetFadeText(DependencyObject element, string? value) =>
        element.SetValue(FadeTextProperty, value ?? string.Empty);

    private static void OnFadeTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block)
        {
            return;
        }

        block.Text = (string)e.NewValue;
        var fade = new DoubleAnimation(0.35, 1, FadeDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        block.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}

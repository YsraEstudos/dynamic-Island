using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace Island.App.Animations;

/// <summary>
/// Makes a borderless window look like a widget that grows into it: the card starts scaled and positioned over the
/// source rectangle (the widget on screen), with the widget's colour, then expands to its real size while the content
/// fades in. Closing plays the same motion backwards. Rectangles are in screen DIPs.
/// </summary>
public sealed class WindowMorph
{
    private readonly FrameworkElement _card;
    private readonly FrameworkElement _content;
    private readonly FrameworkElement? _shadow;
    private readonly Border _background;
    private readonly Color _fromColor;
    private readonly Color _toColor;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _translate = new();

    /// <param name="card">Rounded card that is the visible body of the window (its Background must be a SolidColorBrush border).</param>
    /// <param name="content">Everything inside the card; fades in after the card has grown.</param>
    /// <param name="shadow">Optional sibling that draws the drop shadow; faded in once the morph is done.</param>
    public WindowMorph(Border card, FrameworkElement content, FrameworkElement? shadow, Color fromColor, Color toColor)
    {
        _card = card;
        _background = card;
        _content = content;
        _shadow = shadow;
        _fromColor = fromColor;
        _toColor = toColor;

        var group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_translate);
        _card.RenderTransformOrigin = new Point(0, 0);
        _card.RenderTransform = group;
        _background.Background = new SolidColorBrush(toColor);
    }

    /// <summary>Plays the opening motion. <paramref name="cardRect"/> is where the card ends up on screen.</summary>
    public void Open(Rect cardRect, Rect? source, bool reduceMotion)
    {
        if (reduceMotion)
        {
            Settle();
            return;
        }

        if (source is not { } from || from.Width < 8 || from.Height < 8 || cardRect.Width < 8 || cardRect.Height < 8)
        {
            OpenFromBelow();
            return;
        }

        double sx = from.Width / cardRect.Width;
        double sy = from.Height / cardRect.Height;
        double tx = from.X - cardRect.X;
        double ty = from.Y - cardRect.Y;

        var grow = TimeSpan.FromMilliseconds(400);
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };

        _content.Opacity = 0;
        if (_shadow is not null) _shadow.Opacity = 0;

        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(sx, 1, grow) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(sy, 1, grow) { EasingFunction = ease });
        _translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(tx, 0, grow) { EasingFunction = ease });
        _translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(ty, 0, grow) { EasingFunction = ease });

        var brush = new SolidColorBrush(_fromColor);
        _background.Background = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(_fromColor, _toColor, TimeSpan.FromMilliseconds(320))
        {
            FillBehavior = FillBehavior.HoldEnd
        });

        _content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            BeginTime = TimeSpan.FromMilliseconds(170),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

        FadeShadowIn(TimeSpan.FromMilliseconds(330));
    }

    /// <summary>Plays the closing motion and completes when the window can be closed.</summary>
    public Task CloseAsync(Rect cardRect, Rect? source, bool reduceMotion)
    {
        if (reduceMotion) return Task.CompletedTask;

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contentFade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        _content.BeginAnimation(UIElement.OpacityProperty, contentFade);
        _shadow?.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(110)));

        var length = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        if (source is { } to && to.Width >= 8 && to.Height >= 8 && cardRect.Width >= 8 && cardRect.Height >= 8)
        {
            var scaleX = new DoubleAnimation(to.Width / cardRect.Width, length) { EasingFunction = ease };
            scaleX.Completed += (_, _) => done.TrySetResult();
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to.Height / cardRect.Height, length) { EasingFunction = ease });
            _translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to.X - cardRect.X, length) { EasingFunction = ease });
            _translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(to.Y - cardRect.Y, length) { EasingFunction = ease });
            if (_background.Background is SolidColorBrush brush)
                brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(_fromColor, TimeSpan.FromMilliseconds(220)));
        }
        else
        {
            var fade = new DoubleAnimation(0, length) { EasingFunction = ease };
            fade.Completed += (_, _) => done.TrySetResult();
            _card.BeginAnimation(UIElement.OpacityProperty, fade);
            _translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, length) { EasingFunction = ease });
        }

        return done.Task;
    }

    private void OpenFromBelow()
    {
        var length = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _card.Opacity = 0;
        _card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, length) { EasingFunction = ease });
        _translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, length) { EasingFunction = ease });
        if (_shadow is not null)
        {
            _shadow.Opacity = 0;
            FadeShadowIn(length);
        }
    }

    private void Settle()
    {
        _content.Opacity = 1;
        _card.Opacity = 1;
        if (_shadow is not null) _shadow.Opacity = 1;
    }

    private void FadeShadowIn(TimeSpan delay)
    {
        _shadow?.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            BeginTime = delay
        });
    }
}

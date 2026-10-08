using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Island.App.Animations;
using Size = System.Windows.Size;

namespace Island.App.Widgets;

/// <summary>
/// Spring press feedback for any UIElement: the element scales to <c>pressedScale</c> while the left button is
/// held and springs back on release. The mouse is captured while pressed. <see cref="Clicked"/> fires on release
/// only when the pointer is still over the element. The press also marks the event handled, so a control
/// nested inside another (a chip's remove button, a card's delete pill) does not trigger the parent.
/// </summary>
public sealed class PressFeedback : IFrameClient
{
    private const double Zeta = 0.85;
    private const double SettleSeconds = 0.18;

    private readonly UIElement _target;
    private readonly ScaleTransform _scale = new(1.0, 1.0);
    private readonly SpringValue _spring;
    private readonly double _pressedScale;
    private bool _down;

    public PressFeedback(UIElement target, double pressedScale = 0.92)
    {
        _target = target;
        _pressedScale = pressedScale;
        _spring = new SpringValue(SpringValue.OmegaForSettleTime(SettleSeconds, Zeta), Zeta, 1.0);

        target.RenderTransform = _scale;
        target.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        target.MouseLeftButtonDown += OnDown;
        target.MouseLeftButtonUp += OnUp;
    }

    /// <summary>Raised when a press is released over the element.</summary>
    public event Action? Clicked;

    public bool OnFrame(double dt)
    {
        bool settled = _spring.Advance(dt);
        _scale.ScaleX = _spring.Current;
        _scale.ScaleY = _spring.Current;
        return !settled;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (!_target.IsEnabled) return;

        _down = true;
        _target.CaptureMouse();
        _spring.SetTarget(_pressedScale);
        MotionPump.Request(this);
        e.Handled = true;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_down) return;

        _down = false;
        _target.ReleaseMouseCapture();

        System.Windows.Point p = e.GetPosition(_target);
        Size size = _target.RenderSize;
        bool inside = p.X >= 0.0 && p.Y >= 0.0 && p.X <= size.Width && p.Y <= size.Height;

        _spring.SetTarget(1.0);
        MotionPump.Request(this);
        e.Handled = true;

        if (inside) Clicked?.Invoke();
    }
}

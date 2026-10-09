using System.Windows;
using System.Windows.Media;
using Island.App.Animations;

namespace Island.App.Views.Widgets;

/// <summary>
/// Entrance for the latest screenshot thumbnail: a light spring that fades it in and settles it from 94% scale.
/// It is stepped by the shared <see cref="MotionPump"/>, so it costs nothing once settled.
/// </summary>
internal sealed class CaptureThumbnailMotion : IFrameClient
{
    private const double SettleSeconds = 0.32;
    private const double Zeta = 0.9;
    private const double StartScale = 0.94;

    private readonly UIElement _target;
    private readonly ScaleTransform _scale;
    private readonly SpringValue _opacity = new(SpringValue.OmegaForSettleTime(SettleSeconds, Zeta), Zeta, 1.0);
    private readonly SpringValue _zoom = new(SpringValue.OmegaForSettleTime(SettleSeconds, Zeta), Zeta, 1.0);

    public CaptureThumbnailMotion(UIElement target, ScaleTransform scale)
    {
        _target = target;
        _scale = scale;
    }

    /// <summary>Plays the entrance from invisible to shown. With reduced animations the thumbnail just appears.</summary>
    public void Play(bool reduceAnimations)
    {
        if (reduceAnimations)
        {
            _opacity.Snap(1.0);
            _zoom.Snap(1.0);
            Apply();
            return;
        }

        _opacity.Snap(0.0);
        _zoom.Snap(StartScale);
        _opacity.SetTarget(1.0);
        _zoom.SetTarget(1.0);
        MotionPump.Request(this);
    }

    public bool OnFrame(double dt)
    {
        bool opacitySettled = _opacity.Advance(dt);
        bool zoomSettled = _zoom.Advance(dt);
        Apply();
        return !(opacitySettled && zoomSettled);
    }

    private void Apply()
    {
        _target.Opacity = Math.Clamp(_opacity.Current, 0.0, 1.0);
        _scale.ScaleX = _zoom.Current;
        _scale.ScaleY = _zoom.Current;
    }
}

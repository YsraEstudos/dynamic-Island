using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Island.App.Animations;

namespace Island.App.Controls;

/// <summary>
/// Icon-only button. Press feedback scales the button to 0.92 and springs back on release.
/// <see cref="IsAltShown"/> crossfades (about 150 ms) from <see cref="Icon"/> to <see cref="AltIcon"/>,
/// used for the play/pause morph. The template (Styles/Controls.xaml) draws the icons in a 24 x 24 box.
/// </summary>
public class IconButton : System.Windows.Controls.Button, IFrameClient
{
    private const double PressedScale = 0.92;
    private const double PressZeta = 0.85;
    private const double PressSettleSeconds = 0.18;
    private const double CrossfadeSeconds = 0.15;
    private const double SwapScaleFrom = 0.85;

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(IconButton), new PropertyMetadata(null));

    public static readonly DependencyProperty AltIconProperty = DependencyProperty.Register(
        nameof(AltIcon), typeof(Geometry), typeof(IconButton), new PropertyMetadata(null));

    public static readonly DependencyProperty IsAltShownProperty = DependencyProperty.Register(
        nameof(IsAltShown), typeof(bool), typeof(IconButton), new PropertyMetadata(false, OnIsAltShownChanged));

    public static readonly DependencyProperty IsOutlineProperty = DependencyProperty.Register(
        nameof(IsOutline), typeof(bool), typeof(IconButton), new PropertyMetadata(false));

    private readonly SpringValue _press = new(SpringValue.OmegaForSettleTime(PressSettleSeconds, PressZeta), PressZeta, 1.0);
    private readonly SpringValue _mainFade = new(SpringValue.OmegaForSettleTime(CrossfadeSeconds, 1.0), 1.0, 1.0);
    private readonly SpringValue _altFade = new(SpringValue.OmegaForSettleTime(CrossfadeSeconds, 1.0), 1.0, 0.0);
    private readonly SpringValue _mainScale = new(SpringValue.OmegaForSettleTime(CrossfadeSeconds, 1.0), 1.0, 1.0);
    private readonly SpringValue _altScale = new(SpringValue.OmegaForSettleTime(CrossfadeSeconds, 1.0), 1.0, SwapScaleFrom);

    private ScaleTransform? _pressTransform;
    private ScaleTransform? _mainTransform;
    private ScaleTransform? _altTransform;
    private System.Windows.Shapes.Path? _mainPath;
    private System.Windows.Shapes.Path? _altPath;

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public Geometry? AltIcon
    {
        get => (Geometry?)GetValue(AltIconProperty);
        set => SetValue(AltIconProperty, value);
    }

    /// <summary>When true the alternate icon is shown (crossfaded).</summary>
    public bool IsAltShown
    {
        get => (bool)GetValue(IsAltShownProperty);
        set => SetValue(IsAltShownProperty, value);
    }

    /// <summary>When true the icon is drawn as a 1.8 unit stroke instead of a filled shape.</summary>
    public bool IsOutline
    {
        get => (bool)GetValue(IsOutlineProperty);
        set => SetValue(IsOutlineProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _pressTransform = GetTemplateChild("PART_Scale") as ScaleTransform;
        _mainPath = GetTemplateChild("PART_Icon") as System.Windows.Shapes.Path;
        _altPath = GetTemplateChild("PART_Alt") as System.Windows.Shapes.Path;

        if (_mainPath is not null)
        {
            _mainTransform = new ScaleTransform(1.0, 1.0);
            _mainPath.RenderTransform = _mainTransform;
        }
        if (_altPath is not null)
        {
            _altTransform = new ScaleTransform(SwapScaleFrom, SwapScaleFrom);
            _altPath.RenderTransform = _altTransform;
        }

        ApplyVisuals();
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        SetPressed(true);
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        SetPressed(false);
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SetPressed(false);
    }

    public bool OnFrame(double dt)
    {
        bool pressRest = _press.Advance(dt);
        bool mainFadeRest = _mainFade.Advance(dt);
        bool altFadeRest = _altFade.Advance(dt);
        bool mainScaleRest = _mainScale.Advance(dt);
        bool altScaleRest = _altScale.Advance(dt);
        ApplyVisuals();

        return !(pressRest && mainFadeRest && altFadeRest && mainScaleRest && altScaleRest);
    }

    private static void OnIsAltShownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var button = (IconButton)d;
        bool alt = (bool)e.NewValue;

        // Before the template exists there is nothing to animate: jump straight to the state.
        bool animate = button._mainPath is not null;
        button.Retarget(button._mainFade, alt ? 0.0 : 1.0, animate);
        button.Retarget(button._altFade, alt ? 1.0 : 0.0, animate);
        button.Retarget(button._mainScale, alt ? SwapScaleFrom : 1.0, animate);
        button.Retarget(button._altScale, alt ? 1.0 : SwapScaleFrom, animate);

        if (animate) MotionPump.Request(button);
        else button.ApplyVisuals();
    }

    private void Retarget(SpringValue spring, double target, bool animate)
    {
        if (animate) spring.SetTarget(target);
        else spring.Snap(target);
    }

    private void SetPressed(bool pressed)
    {
        if (pressed && !IsEnabled) return;
        _press.SetTarget(pressed ? PressedScale : 1.0);
        MotionPump.Request(this);
    }

    private void ApplyVisuals()
    {
        if (_pressTransform is not null)
        {
            _pressTransform.ScaleX = _press.Current;
            _pressTransform.ScaleY = _press.Current;
        }
        if (_mainPath is not null)
        {
            _mainPath.Opacity = Math.Clamp(_mainFade.Current, 0.0, 1.0);
        }
        if (_mainTransform is not null)
        {
            _mainTransform.ScaleX = _mainScale.Current;
            _mainTransform.ScaleY = _mainScale.Current;
        }
        if (_altPath is not null)
        {
            _altPath.Opacity = Math.Clamp(_altFade.Current, 0.0, 1.0);
        }
        if (_altTransform is not null)
        {
            _altTransform.ScaleX = _altScale.Current;
            _altTransform.ScaleY = _altScale.Current;
        }
    }
}

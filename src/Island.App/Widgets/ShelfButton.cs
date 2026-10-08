using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Island.App.Animations;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Island.App.Widgets;

/// <summary>
/// Compact shelf button: optional icon (crossfading to <see cref="AltIcon"/> when <see cref="IsAltShown"/> is set),
/// optional text label, optional round background. Press feedback is a spring scale (see <see cref="PressFeedback"/>).
/// Use Background / CornerRadius from the Border base to shape it.
/// </summary>
public sealed class ShelfButton : Border, IFrameClient
{
    private const double FadeSeconds = 0.15;

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(ShelfButton), new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty AltIconProperty = DependencyProperty.Register(
        nameof(AltIcon), typeof(Geometry), typeof(ShelfButton), new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty IsAltShownProperty = DependencyProperty.Register(
        nameof(IsAltShown), typeof(bool), typeof(ShelfButton), new PropertyMetadata(false, OnIsAltShownChanged));

    public static readonly DependencyProperty IsFilledProperty = DependencyProperty.Register(
        nameof(IsFilled), typeof(bool), typeof(ShelfButton), new PropertyMetadata(false, OnVisualChanged));

    public static readonly DependencyProperty IconBrushProperty = DependencyProperty.Register(
        nameof(IconBrush), typeof(Brush), typeof(ShelfButton), new PropertyMetadata(Brushes.White, OnVisualChanged));

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(ShelfButton), new PropertyMetadata(16.0, OnVisualChanged));

    public static readonly DependencyProperty LabelTextProperty = DependencyProperty.Register(
        nameof(LabelText), typeof(string), typeof(ShelfButton), new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(ShelfButton), new PropertyMetadata(Brushes.White, OnVisualChanged));

    private readonly PressFeedback _press;
    private readonly SpringValue _fade = new(SpringValue.OmegaForSettleTime(FadeSeconds, 1.0), 1.0, 0.0);
    private readonly Path _mainPath = new();
    private readonly Path _altPath = new();
    private readonly Viewbox _iconBox;
    private readonly TextBlock _label;

    public ShelfButton()
    {
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;

        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(_mainPath);
        canvas.Children.Add(_altPath);
        _iconBox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = canvas,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _label = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };
        _label.SetResourceReference(TextBlock.FontFamilyProperty, "IslandFontFamily");

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(_iconBox);
        panel.Children.Add(_label);
        Child = panel;

        _press = new PressFeedback(this, 0.9);
        _press.Clicked += () => Click?.Invoke();

        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1.0 : 0.4;
        ApplyVisuals();
    }

    /// <summary>Raised when the button is pressed and released over itself.</summary>
    public event Action? Click;

    /// <summary>Icon Geometry (24-unit box).</summary>
    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Icon shown while <see cref="IsAltShown"/> is true, crossfaded.</summary>
    public Geometry? AltIcon
    {
        get => (Geometry?)GetValue(AltIconProperty);
        set => SetValue(AltIconProperty, value);
    }

    public bool IsAltShown
    {
        get => (bool)GetValue(IsAltShownProperty);
        set => SetValue(IsAltShownProperty, value);
    }

    /// <summary>Draws the icon filled instead of as a 1.8 unit stroke.</summary>
    public bool IsFilled
    {
        get => (bool)GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    public Brush IconBrush
    {
        get => (Brush)GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>Optional text shown after the icon (or alone).</summary>
    public string? LabelText
    {
        get => (string?)GetValue(LabelTextProperty);
        set => SetValue(LabelTextProperty, value);
    }

    public Brush LabelBrush
    {
        get => (Brush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public bool OnFrame(double dt)
    {
        bool settled = _fade.Advance(dt);
        ApplyFade();
        return !settled;
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ShelfButton)d).ApplyVisuals();

    private static void OnIsAltShownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var button = (ShelfButton)d;
        double target = (bool)e.NewValue ? 1.0 : 0.0;

        if (button.IsLoaded)
        {
            button._fade.SetTarget(target);
            MotionPump.Request(button);
        }
        else
        {
            button._fade.Snap(target);
        }
        button.ApplyFade();
    }

    private void ApplyVisuals()
    {
        bool hasIcon = Icon is not null || AltIcon is not null;
        bool hasLabel = !string.IsNullOrEmpty(LabelText);

        _mainPath.Data = Icon;
        _altPath.Data = AltIcon;
        _mainPath.Fill = IsFilled ? IconBrush : null;
        _mainPath.Stroke = IsFilled ? null : IconBrush;
        _mainPath.StrokeThickness = IsFilled ? 0.0 : 1.8;
        _mainPath.StrokeLineJoin = PenLineJoin.Round;
        _mainPath.StrokeStartLineCap = PenLineCap.Round;
        _mainPath.StrokeEndLineCap = PenLineCap.Round;
        _altPath.Fill = IsFilled ? IconBrush : null;
        _altPath.Stroke = IsFilled ? null : IconBrush;
        _altPath.StrokeThickness = IsFilled ? 0.0 : 1.8;
        _altPath.StrokeLineJoin = PenLineJoin.Round;
        _altPath.StrokeStartLineCap = PenLineCap.Round;
        _altPath.StrokeEndLineCap = PenLineCap.Round;

        _iconBox.Width = IconSize;
        _iconBox.Height = IconSize;
        _iconBox.Visibility = hasIcon ? Visibility.Visible : Visibility.Collapsed;

        _label.Text = LabelText ?? string.Empty;
        _label.Foreground = LabelBrush;
        _label.Visibility = hasLabel ? Visibility.Visible : Visibility.Collapsed;
        _label.Margin = hasIcon && hasLabel ? new Thickness(6, 0, 0, 0) : new Thickness(0);

        ApplyFade();
    }

    private void ApplyFade()
    {
        double f = Math.Clamp(_fade.Current, 0.0, 1.0);
        _mainPath.Opacity = 1.0 - f;
        _altPath.Opacity = f;
    }
}

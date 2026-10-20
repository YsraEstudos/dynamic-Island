using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Island.Core.Files;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace Island.App.Shell;

public partial class FileOrbitWindow
{
    private sealed record ActionMotion(FrameworkElement Visual, ScaleTransform Scale, RotateTransform Rotation, TranslateTransform Offset);
    private readonly Dictionary<Button, ActionMotion> _actionMotion = [];
    private bool _closingAnimation;
    private bool _closeReady;
    private bool _revealed;
    private Rect? _sourceCapsule;
    private Rect? _radialBounds;
    private Rect? _workArea;

    public void PlaceAt(Point anchor, Rect workArea, Rect? sourceCapsule = null)
    {
        _workArea = workArea;
        Width = Math.Min(420, workArea.Width); Height = Math.Min(520, workArea.Height);
        Left = Math.Clamp(anchor.X - Width / 2, workArea.Left, workArea.Right - Width);
        Top = Math.Clamp(anchor.Y - 60 - 260 * (Height - 60) / 560, workArea.Top, workArea.Bottom - Height);
        double scale = Math.Min(Width / 520, Height / 560);
        RevealOrigin = new Point((anchor.X - Left - (Width - 520 * scale) / 2) / scale,
            (anchor.Y - Top - (Height - 560 * scale) / 2) / scale);
        // The capsule that opened the menu (the island) in window coordinates: the first frame starts exactly there.
        _sourceCapsule = sourceCapsule is { } source && source.Width >= 24 && source.Height >= 12
            ? new Rect(source.X - Left, source.Y - Top, source.Width, source.Height)
            : null;
    }

    /// <summary>
    /// First reveal: the hint capsule grows out of the island's rectangle, then the ring blooms from its hub.
    /// Without a source capsule (for example a raw placement in tests) it falls back to the normal opening.
    /// </summary>
    private void AnimateReveal()
    {
        if (_revealed) return;
        _revealed = true;
        if (_reduce || _sourceCapsule is not { } source)
        {
            AnimateOrbit();
            return;
        }

        const double finalWidth = 210, finalHeight = 46, finalRadius = 26;
        double startWidth = Math.Clamp(source.Width, 96, 220);
        double startHeight = Math.Clamp(source.Height, 26, 48);
        // Keep the centre line identical in the first frame, even when the window was clamped near a screen edge.
        var slide = new TranslateTransform(source.X - (Width - startWidth) / 2, source.Y - (60 - startHeight) / 2);

        HintPill.MinWidth = 0;
        HintPill.RenderTransform = slide;
        HintPill.Width = startWidth; HintPill.Height = startHeight;
        HintPill.CornerRadius = new CornerRadius(finalRadius);
        var background = new SolidColorBrush(System.Windows.Media.Colors.Black);
        HintPill.Background = background;
        var border = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 124, 140, 255));
        HintPill.BorderBrush = border;
        HintPill.BorderThickness = new Thickness(1);
        HintText.Opacity = 0;
        var textSlide = new TranslateTransform(0, 5);
        HintText.RenderTransform = textSlide;

        var grow = TimeSpan.FromMilliseconds(460);
        var ease = new ExponentialEase { Exponent = 5, EasingMode = EasingMode.EaseOut };
        HintPill.BeginAnimation(WidthProperty, new DoubleAnimation(finalWidth, grow) { EasingFunction = ease });
        HintPill.BeginAnimation(HeightProperty, new DoubleAnimation(finalHeight, grow) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, grow) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, grow) { EasingFunction = ease });
        background.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(System.Windows.Media.Color.FromRgb(13, 14, 20), TimeSpan.FromMilliseconds(360)) { BeginTime = TimeSpan.FromMilliseconds(80) });
        border.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(System.Windows.Media.Color.FromArgb(0x60, 124, 140, 255), TimeSpan.FromMilliseconds(360)) { BeginTime = TimeSpan.FromMilliseconds(120) });
        HintText.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = TimeSpan.FromMilliseconds(240) });
        textSlide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(240)) { BeginTime = TimeSpan.FromMilliseconds(240), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        foreach (FrameworkElement footer in new FrameworkElement[] { OrbitTrayButton, OrbitCloseButton })
        {
            footer.Opacity = 0;
            var slideUp = new TranslateTransform(0, 8);
            footer.RenderTransform = slideUp;
            footer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(260) });
            slideUp.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(260), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        AnimateOrbit(reveal: true);
    }

    private void ResizePresentation(bool details)
    {
        if (details)
        {
            if (_radialBounds is not null) return;
            _radialBounds = new Rect(Left, Top, Width, Height);
            double centerX = Left + Width / 2, capsuleTop = Top;
            Width = Math.Min(576, _workArea?.Width ?? 576); Height = Math.Min(680, _workArea?.Height ?? 680);
            Left = centerX - Width / 2; Top = capsuleTop;
            if (_workArea is { } area)
            {
                Left = Math.Clamp(Left, area.Left, area.Right - Width);
                Top = Math.Clamp(Top, area.Top, area.Bottom - Height);
            }
        }
        else if (_radialBounds is { } bounds)
        {
            Left = bounds.Left; Top = bounds.Top; Width = bounds.Width; Height = bounds.Height;
            _radialBounds = null;
        }
    }

    private ActionMotion MotionFor(Button button)
    {
        if (_actionMotion.TryGetValue(button, out var motion)) return motion;
        button.ApplyTemplate();
        var visual = (FrameworkElement)button.Template.FindName("ActionVisual", button);
        var scale = new ScaleTransform(); var offset = new TranslateTransform(); var rotation = new RotateTransform();
        visual.RenderTransform = new TransformGroup { Children = { scale, rotation, offset } };
        motion = new ActionMotion(visual, scale, rotation, offset); _actionMotion.Add(button, motion);
        return motion;
    }

    private IEnumerable<Button> ActionButtons => OrbitCanvas.Children.OfType<Button>().Where(b => b.Tag is FileAction);

    private void AnimateOrbit(bool reveal = false)
    {
        int index = 0;
        foreach (var button in ActionButtons)
        {
            var motion = MotionFor(button);
            var content = (FrameworkElement)button.Content;
            var shape = (System.Windows.Shapes.Path)content.FindName("SectorShape");
            shape.Fill = shape.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 27, 32));
            button.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
            motion.Visual.Effect = null;
            motion.Offset.BeginAnimation(TranslateTransform.XProperty, null);
            motion.Offset.BeginAnimation(TranslateTransform.YProperty, null);
            motion.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            motion.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            motion.Rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            motion.Visual.BeginAnimation(OpacityProperty, null);
            motion.Offset.X = motion.Offset.Y = 0;
            motion.Scale.ScaleX = motion.Scale.ScaleY = 1;
            motion.Rotation.Angle = 0;
            motion.Visual.Opacity = 1;
            ((FrameworkElement)content.FindName("ActionGlyph")).RenderTransform = Transform.Identity;
            button.IsHitTestVisible = _reduce;
            if (_reduce) continue;

            // Each sector slides out from behind the hub instead of dropping in, like the island growing content.
            var sector = (RadialSectorButton)button;
            var origin = RadialSectorButton.Polar(sector.Angle, 164);
            TimeSpan delay = TimeSpan.FromMilliseconds((reveal ? 90 : 40) + index++ * 38);
            motion.Visual.Opacity = 0;
            motion.Scale.ScaleX = motion.Scale.ScaleY = .64;
            motion.Rotation.Angle = -16;
            motion.Offset.X = 260 - origin.X;
            motion.Offset.Y = 260 - origin.Y;
            var bloom = TimeSpan.FromMilliseconds(680);
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .28 };
            DoubleAnimation Move(double to) => new(to, bloom) { BeginTime = delay, EasingFunction = ease };
            var settle = Move(0);
            settle.Completed += (_, _) => { if (!_closingAnimation) button.IsHitTestVisible = true; };
            motion.Rotation.BeginAnimation(RotateTransform.AngleProperty, Move(0));
            motion.Offset.BeginAnimation(TranslateTransform.XProperty, settle);
            motion.Offset.BeginAnimation(TranslateTransform.YProperty, Move(0));
            motion.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Move(1));
            motion.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, Move(1));
            motion.Visual.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
            { BeginTime = delay + TimeSpan.FromMilliseconds(30) });
        }

        if (!reveal) ResetHub();
        if (_reduce)
        {
            Hub.BeginAnimation(OpacityProperty, null); Hub.Opacity = 1; Hub.RenderTransform = Transform.Identity;
            OrbitDecoration.BeginAnimation(OpacityProperty, null); OrbitDecoration.Opacity = 1; OrbitDecoration.RenderTransform = Transform.Identity;
            return;
        }

        var hubScale = new ScaleTransform(reveal ? .84 : .4, reveal ? .84 : .4);
        Hub.RenderTransform = hubScale;
        var hubEnter = new DoubleAnimation(1, TimeSpan.FromMilliseconds(reveal ? 460 : 800))
        { BeginTime = TimeSpan.FromMilliseconds(reveal ? 90 : 100), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .7 } };
        hubScale.BeginAnimation(ScaleTransform.ScaleXProperty, hubEnter); hubScale.BeginAnimation(ScaleTransform.ScaleYProperty, hubEnter);
        Hub.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(reveal ? 240 : 400))
        { BeginTime = TimeSpan.FromMilliseconds(reveal ? 70 : 100) });
        OrbitDecoration.Opacity = 0;
        OrbitDecoration.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { BeginTime = TimeSpan.FromMilliseconds(140) });
        OrbitDecoration.RenderTransform = new RotateTransform();
        ((RotateTransform)OrbitDecoration.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(50)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private void Hover(Button button, bool on)
    {
        if (_confirmingSelection && !on) return;
        if (_closingAnimation || button is not RadialSectorButton sector || button.Tag is not FileAction action) return;
        var motion = MotionFor(button);
        var content = (FrameworkElement)button.Content;
        var shape = (System.Windows.Shapes.Path)content.FindName("SectorShape");
        var color = on ? sector.Accent : System.Windows.Media.Color.FromRgb(25, 27, 32);
        if (_reduce) { shape.Fill = new SolidColorBrush(color); shape.Stroke = shape.Fill; }
        else
        {
            if (shape.Fill is not SolidColorBrush fill || fill.IsFrozen) shape.Fill = new SolidColorBrush(color);
            if (shape.Stroke is not SolidColorBrush stroke || stroke.IsFrozen) shape.Stroke = new SolidColorBrush(color);
            ((SolidColorBrush)shape.Fill).BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(250)));
            ((SolidColorBrush)shape.Stroke).BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(250)));
        }
        motion.Visual.Effect = on ? new System.Windows.Media.Effects.DropShadowEffect { Color = sector.Accent, BlurRadius = 16, ShadowDepth = 0, Opacity = .7 } : null;
        if (on)
        {
            HubTitle.Text = FileOrbitActionVisuals.Label(action); HubDetail.Text = HubDescription(action);
            HubRing.Stroke = new SolidColorBrush(sector.Accent);
            button.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(11, 12, 16));
            HintText.Text = "↓   " + FileActionCatalog.Label(action);
            HintPill.BorderBrush = new SolidColorBrush(sector.Accent);
            AnimateHint(true);
        }
        else { button.ClearValue(System.Windows.Controls.Control.ForegroundProperty); ResetHub(); }
        if (_reduce) return;
        var animation = new DoubleAnimation(on ? 1.045 : 1, TimeSpan.FromMilliseconds(300))
        { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .8 } };
        motion.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        motion.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        var direction = RadialSectorButton.Polar(sector.Angle, on ? 10 : 0);
        motion.Offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(direction.X - 260, TimeSpan.FromMilliseconds(300)));
        motion.Offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(direction.Y - 260, TimeSpan.FromMilliseconds(300)));
        var glyph = (FrameworkElement)content.FindName("ActionGlyph");
        var iconScale = glyph.RenderTransform as ScaleTransform ?? new ScaleTransform(); glyph.RenderTransform = iconScale;
        var iconAnimation = new DoubleAnimation(on ? 1.18 : 1, TimeSpan.FromMilliseconds(350)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .8 } };
        iconScale.BeginAnimation(ScaleTransform.ScaleXProperty, iconAnimation); iconScale.BeginAnimation(ScaleTransform.ScaleYProperty, iconAnimation);
    }

    private void ResetHub()
    {
        AnimateHint(false);
        HintText.Text = "↓   Escolha uma ação";
        HintPill.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 140, 255));
        HubTitle.Text = "Escolha"; HubDetail.Text = "arraste até uma ação";
        HubRing.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(91, 140, 255));
    }

    private void AnimateHint(bool active)
    {
        double width = active ? 256 : 210, height = active ? 52 : 46;
        if (_reduce) { HintPill.Width = width; HintPill.Height = height; return; }
        var ease = new BackEase { Amplitude = .35, EasingMode = EasingMode.EaseOut };
        HintPill.BeginAnimation(WidthProperty, new DoubleAnimation(HintPill.ActualWidth, width, TimeSpan.FromMilliseconds(700)) { EasingFunction = ease });
        HintPill.BeginAnimation(HeightProperty, new DoubleAnimation(HintPill.ActualHeight, height, TimeSpan.FromMilliseconds(700)) { EasingFunction = ease });
    }

    private static string HubDescription(FileAction action) => action switch
    {
        FileAction.Rename => "copiar com novos nomes", FileAction.Duplicates => "comparar conteúdo",
        FileAction.Package => "criar arquivo ZIP", FileAction.Index => "listar seus arquivos",
        FileAction.ContactSheet => "criar folha de contato", FileAction.Images => "converter e ajustar",
        FileAction.ImagesPdf or FileAction.OfficePdf => "criar um documento",
        FileAction.MergePdf => "reunir documentos", FileAction.SplitPdf => "separar páginas",
        FileAction.CompressPdf => "reduzir o tamanho", FileAction.Audio => "converter ou cortar",
        FileAction.Video => "converter e extrair", FileAction.Extract => "abrir um pacote", _ => "converter seus dados"
    };

    private void AnimateClose(CancelEventArgs args)
    {
        if (_reduce || _closeReady || OrbitPanel.Visibility != Visibility.Visible || !IsVisible) return;
        args.Cancel = true;
        if (_closingAnimation) return;
        _closingAnimation = true;
        IsHitTestVisible = false;
        DismissStarted?.Invoke();
        var collapse = TimeSpan.FromMilliseconds(450);
        foreach (var button in ActionButtons)
        {
            var motion = MotionFor(button);
            var sector = (RadialSectorButton)button;
            var origin = RadialSectorButton.Polar(sector.Angle, 164);
            motion.Offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(260 - origin.X, collapse));
            motion.Offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(260 - origin.Y, collapse));
            motion.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.62, collapse));
            motion.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.62, collapse));
            motion.Rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-16, collapse));
        }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(350));
        fade.Completed += (_, _) => { _closeReady = true; Close(); };
        BeginAnimation(OpacityProperty, fade);
    }
}

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
    private Rect? _radialBounds;
    private Rect? _workArea;

    public void PlaceAt(Point anchor, Rect workArea)
    {
        _workArea = workArea;
        Width = Math.Min(420, workArea.Width); Height = Math.Min(460, workArea.Height);
        Left = Math.Clamp(anchor.X - Width / 2, workArea.Left, workArea.Right - Width);
        Top = Math.Clamp(anchor.Y - 260 * Height / 560, workArea.Top, workArea.Bottom - Height);
        double scale = Math.Min(Width / 520, Height / 560);
        RevealOrigin = new Point((anchor.X - Left - (Width - 520 * scale) / 2) / scale,
            (anchor.Y - Top - (Height - 560 * scale) / 2) / scale);
    }

    private void ResizePresentation(bool details)
    {
        if (details)
        {
            if (_radialBounds is not null) return;
            _radialBounds = new Rect(Left, Top, Width, Height);
            double centerX = Left + Width / 2, centerY = Top + Height / 2;
            Width = Math.Min(680, _workArea?.Width ?? 680); Height = Math.Min(620, _workArea?.Height ?? 620);
            Left = centerX - Width / 2; Top = centerY - Height / 2;
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

    private void AnimateOrbit()
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
            motion.Offset.X = motion.Offset.Y = 0;
            ((FrameworkElement)content.FindName("ActionGlyph")).RenderTransform = Transform.Identity;
            button.IsHitTestVisible = _reduce;
            if (_reduce) continue;
            TimeSpan delay = TimeSpan.FromMilliseconds(index++ * 55);
            motion.Visual.Opacity = 0;
            motion.Scale.ScaleX = motion.Scale.ScaleY = .5;
            motion.Rotation.Angle = -26;
            DoubleAnimation Move(double from, double to) => new(from, to, TimeSpan.FromMilliseconds(700))
            { BeginTime = delay, EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .65 } };
            var reveal = Move(-26, 0);
            reveal.Completed += (_, _) => { if (!_closingAnimation) button.IsHitTestVisible = true; };
            motion.Rotation.BeginAnimation(RotateTransform.AngleProperty, reveal);
            motion.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Move(.5, 1));
            motion.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, Move(.5, 1));
            motion.Visual.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)) { BeginTime = delay });
        }
        ResetHub();
        if (!_reduce)
        {
            var hubScale = new ScaleTransform(); Hub.RenderTransform = hubScale;
            var enter = new DoubleAnimation(.4, 1, TimeSpan.FromMilliseconds(700))
            { BeginTime = TimeSpan.FromMilliseconds(100), EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .7 } };
            hubScale.BeginAnimation(ScaleTransform.ScaleXProperty, enter); hubScale.BeginAnimation(ScaleTransform.ScaleYProperty, enter);
            Hub.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { BeginTime = TimeSpan.FromMilliseconds(100) });
            OrbitDecoration.RenderTransform = new RotateTransform();
            ((RotateTransform)OrbitDecoration.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(50)) { RepeatBehavior = RepeatBehavior.Forever });
        }
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
            button.Foreground = System.Windows.Media.Brushes.White;
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
        HubTitle.Text = "Escolha"; HubDetail.Text = "arraste até uma ação";
        HubRing.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(91, 140, 255));
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
        foreach (var button in ActionButtons)
        {
            var motion = MotionFor(button);
            motion.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.5, TimeSpan.FromMilliseconds(450)));
            motion.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.5, TimeSpan.FromMilliseconds(450)));
            motion.Rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-26, TimeSpan.FromMilliseconds(450)));
        }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(350));
        fade.Completed += (_, _) => { _closeReady = true; Close(); };
        BeginAnimation(OpacityProperty, fade);
    }
}

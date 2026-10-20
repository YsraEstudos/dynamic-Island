using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Island.Core.Files;
using Button = System.Windows.Controls.Button;

namespace Island.App.Shell;

public partial class FileOrbitWindow
{
    private DispatcherTimer? _selectionTimer;
    private bool _confirmingSelection;

    private void PickAction(Button button, FileAction action)
    {
        if (_closingAnimation || _selectionTimer is { IsEnabled: true }) return;
        if (_reduce) { SelectAction(action); return; }
        var sector = (RadialSectorButton)button;
        Hover(button, true);
        _confirmingSelection = true;
        foreach (var target in ActionButtons) target.IsHitTestVisible = false;
        var shape = (System.Windows.Shapes.Path)((FrameworkElement)button.Content).FindName("SectorShape");
        var accent = sector.Accent;
        var bright = System.Windows.Media.Color.FromRgb((byte)Math.Min(255, accent.R * 1.6),
            (byte)Math.Min(255, accent.G * 1.6), (byte)Math.Min(255, accent.B * 1.6));
        var flash = new ColorAnimationUsingKeyFrames();
        flash.KeyFrames.Add(new LinearColorKeyFrame(accent, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        flash.KeyFrames.Add(new LinearColorKeyFrame(bright, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        flash.KeyFrames.Add(new LinearColorKeyFrame(accent, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280))));
        var brush = new SolidColorBrush(accent); shape.Fill = shape.Stroke = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, flash);
        var origin = RadialSectorButton.Polar(sector.Angle, 164);
        FeedbackCanvas.Children.Clear();
        for (int i = 0; i < 16; i++)
        {
            double direction = i * Math.PI * 2 / 16, distance = 50 + i % 4 * 18;
            var particle = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(accent), RenderTransformOrigin = new System.Windows.Point(.5, .5) };
            Canvas.SetLeft(particle, origin.X - 4); Canvas.SetTop(particle, origin.Y - 4);
            var offset = new TranslateTransform(); var scale = new ScaleTransform();
            particle.RenderTransform = new TransformGroup { Children = { scale, offset } };
            FeedbackCanvas.Children.Add(particle);
            var duration = TimeSpan.FromMilliseconds(800);
            offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(Math.Cos(direction) * distance, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Math.Sin(direction) * distance, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.2, duration));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.2, duration));
            particle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, duration));
        }
        _selectionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        _selectionTimer.Tick += (_, _) =>
        {
            _selectionTimer.Stop(); _confirmingSelection = false; FeedbackCanvas.Children.Clear();
            if (IsVisible && !_closingAnimation) SelectAction(action);
        };
        _selectionTimer.Start();
        Closed += (_, _) => _selectionTimer?.Stop();
    }
}

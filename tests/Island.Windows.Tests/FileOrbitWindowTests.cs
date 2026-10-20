using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.App.Shell;

namespace Island.Windows.Tests;

[Collection("Calendar WPF")]
public sealed class FileOrbitWindowTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Orbit_renders_compatible_actions_and_selection_does_not_execute(bool reduce)
    {
        WpfStaTestHost.Run(_ =>
        {
            var window = WpfStaTestHost.KeepOffscreen(new FileOrbitWindow(["example.png"], reduce));
            try
            {
                window.Show(); window.UpdateLayout();
                var canvas = (Canvas)window.FindName("OrbitCanvas");
                var buttons = canvas.Children.OfType<Button>().Where(b => b.Tag is Island.Core.Files.FileAction).ToArray();
                if (!reduce)
                {
                    Assert.All(buttons, button => Assert.False(button.IsHitTestVisible));
                    Pump(650);
                }
                Assert.All(buttons, button => Assert.True(button.IsHitTestVisible));
                Assert.Equal(7, buttons.Length);
                Assert.DoesNotContain(buttons, b => b.Content.ToString()!.Contains("Áudio"));
                // The desktop must remain visible and clickable between the floating actions.
                var shell = (Border)window.FindName("Shell");
                Assert.True(shell.Background is null || shell.Background.Opacity == 0);
                Assert.True(shell.BorderThickness == new Thickness(0));
                Assert.Null(canvas.Background);
                var transparent = new RenderTargetBitmap(720, 640, 96, 96, PixelFormats.Pbgra32);
                transparent.Render(window);
                byte[] rgba = new byte[720 * 640 * 4]; transparent.CopyPixels(rgba, 720 * 4, 0);
                Assert.Equal(0, rgba[(320 * 720 + 360) * 4 + 3]);
                double[] radii = buttons.Select(b => Math.Sqrt(Math.Pow(Canvas.GetLeft(b) + b.Width / 2 - 360, 2)
                    + Math.Pow(Canvas.GetTop(b) + b.Height / 2 - 320, 2))).ToArray();
                Assert.All(radii, radius => Assert.InRange(radius, 219, 221));
                buttons.First(b => b.Tag is Island.Core.Files.FileAction.Images).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("OptionsPanel")).Visibility);
                Assert.True(((Button)window.FindName("RunButton")).IsEnabled);
                Assert.False(window.IsRunning);
                if (!reduce) Pump(300);
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap(720, 640, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var bytes = new byte[720 * 640 * 4]; bitmap.CopyPixels(bytes, 720 * 4, 0);
                Assert.Contains(bytes, b => b > 180);
                if (Environment.GetEnvironmentVariable("FILE_ORBIT_PREVIEW_DIR") is { Length: > 0 } dir)
                {
                    Directory.CreateDirectory(dir);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(dir, "file-orbit-options.png")); encoder.Save(stream);
                    ((Button)window.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                    if (!reduce) Pump(650);
                    var orbit = new RenderTargetBitmap(720, 640, 96, 96, PixelFormats.Pbgra32); orbit.Render(window);
                    var second = new PngBitmapEncoder(); second.Frames.Add(BitmapFrame.Create(orbit));
                    using var other = File.Create(Path.Combine(dir, "file-orbit.png")); second.Save(other);
                    // A checkerboard preview makes the verified alpha channel visible in image viewers.
                    var tiles = new DrawingGroup();
                    tiles.Children.Add(new GeometryDrawing(System.Windows.Media.Brushes.WhiteSmoke, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
                    tiles.Children.Add(new GeometryDrawing(System.Windows.Media.Brushes.LightGray, null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
                    tiles.Children.Add(new GeometryDrawing(System.Windows.Media.Brushes.LightGray, null, new RectangleGeometry(new Rect(12, 12, 12, 12))));
                    var checker = new DrawingBrush(tiles) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 24, 24), ViewportUnits = BrushMappingMode.Absolute };
                    var visual = new DrawingVisual();
                    using (var drawing = visual.RenderOpen()) { drawing.DrawRectangle(checker, null, new Rect(0, 0, 720, 640)); drawing.DrawImage(orbit, new Rect(0, 0, 720, 640)); }
                    var preview = new RenderTargetBitmap(720, 640, 96, 96, PixelFormats.Pbgra32); preview.Render(visual);
                    var checkedEncoder = new PngBitmapEncoder(); checkedEncoder.Frames.Add(BitmapFrame.Create(preview));
                    using var checkedStream = File.Create(Path.Combine(dir, "file-orbit-transparency.png")); checkedEncoder.Save(checkedStream);
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Drag_preview_does_not_activate_or_execute_and_drop_keeps_choices_open()
    {
        WpfStaTestHost.Run(_ =>
        {
            var window = WpfStaTestHost.KeepOffscreen(new FileOrbitWindow(["example.png"], true));
            try
            {
                window.BeginDragPreview(); window.Show();
                Assert.True(window.IsDragPreview);
                Assert.False(window.ShowActivated);
                Assert.False(window.IsRunning);
                window.CompleteDrop();
                Assert.False(window.IsDragPreview);
                Assert.True(window.IsVisible);
                Assert.False(window.IsRunning);
            }
            finally { window.Close(); }
        });
    }

    private static void Pump(int milliseconds)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}

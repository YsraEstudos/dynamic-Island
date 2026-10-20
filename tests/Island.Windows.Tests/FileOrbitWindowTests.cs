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
                Assert.True(new Typeface(window.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out var font));
                Assert.Contains(font.FamilyNames.Values, name => name.Contains("Sora", StringComparison.OrdinalIgnoreCase));
                Assert.InRange(window.Width, 410, 430);
                Assert.InRange(window.Height, 450, 470);
                var canvas = (Canvas)window.FindName("OrbitCanvas");
                var buttons = canvas.Children.OfType<Button>().Where(b => b.Tag is Island.Core.Files.FileAction).ToArray();
                if (!reduce)
                {
                    Assert.All(buttons, button => Assert.True(button.RenderTransform.Value.IsIdentity));
                    Pump(1150);
                }
                Assert.All(buttons, button => Assert.True(button.IsHitTestVisible));
                Assert.Equal(7, buttons.Length);
                Assert.DoesNotContain(buttons, b => b.Content.ToString()!.Contains("Áudio"));
                // The desktop must remain visible and clickable between the floating actions.
                var shell = (Border)window.FindName("Shell");
                Assert.True(shell.Background is null || shell.Background.Opacity == 0);
                Assert.True(shell.BorderThickness == new Thickness(0));
                Assert.Null(canvas.Background);
                var transparent = new RenderTargetBitmap(420, 460, 96, 96, PixelFormats.Pbgra32);
                transparent.Render(window);
                byte[] rgba = new byte[420 * 460 * 4]; transparent.CopyPixels(rgba, 420 * 4, 0);
                Assert.Equal(0, rgba[3]); // No page background: only sectors and the reference's hub.
                Assert.Equal("Escolha", ((TextBlock)window.FindName("HubTitle")).Text);
                Assert.All(buttons, button =>
                {
                    Assert.Equal(520, button.Width);
                    Assert.NotNull(button.Template.FindName("ActionVisual", button));
                    Assert.NotNull(((FrameworkElement)button.Content).FindName("ActionGlyph"));
                    var sector = Assert.IsType<Island.App.Shell.RadialSectorButton>(button);
                    Assert.False(sector.Sector.FillContains(new System.Windows.Point(260, 260)));
                });
                buttons.First(b => b.Tag is Island.Core.Files.FileAction.Images).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!reduce) Pump(400);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("OptionsPanel")).Visibility);
                Assert.True(((Button)window.FindName("RunButton")).IsEnabled);
                Assert.False(window.IsRunning);
                Assert.True(window.Width >= 640);
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
                    if (!reduce) Pump(1150);
                    Assert.Equal(420, window.Width); Assert.Equal(460, window.Height);
                    var orbit = new RenderTargetBitmap(420, 460, 96, 96, PixelFormats.Pbgra32); orbit.Render(window);
                    var second = new PngBitmapEncoder(); second.Frames.Add(BitmapFrame.Create(orbit));
                    using var other = File.Create(Path.Combine(dir, "file-orbit.png")); second.Save(other);
                    // A checkerboard preview makes the verified alpha channel visible in image viewers.
                    var tiles = new DrawingGroup();
                    tiles.Children.Add(new GeometryDrawing(System.Windows.Media.Brushes.WhiteSmoke, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
                    tiles.Children.Add(new GeometryDrawing(System.Windows.Media.Brushes.LightGray, null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
                    tiles.Children.Add(new GeometryDrawing(System.Windows.Media.Brushes.LightGray, null, new RectangleGeometry(new Rect(12, 12, 12, 12))));
                    var checker = new DrawingBrush(tiles) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 24, 24), ViewportUnits = BrushMappingMode.Absolute };
                    var visual = new DrawingVisual();
                    using (var drawing = visual.RenderOpen()) { drawing.DrawRectangle(checker, null, new Rect(0, 0, 420, 460)); drawing.DrawImage(orbit, new Rect(0, 0, 420, 460)); }
                    var preview = new RenderTargetBitmap(420, 460, 96, 96, PixelFormats.Pbgra32); preview.Render(visual);
                    var checkedEncoder = new PngBitmapEncoder(); checkedEncoder.Frames.Add(BitmapFrame.Create(preview));
                    using var checkedStream = File.Create(Path.Combine(dir, "file-orbit-transparency.png")); checkedEncoder.Save(checkedStream);
                    buttons[1].RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                    { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
                    if (!reduce) Pump(400);
                    window.UpdateLayout();
                    Assert.Equal("Duplicados", ((TextBlock)window.FindName("HubTitle")).Text);
                    var highlighted = new RenderTargetBitmap(420, 460, 96, 96, PixelFormats.Pbgra32); highlighted.Render(window);
                    using (var drawing = visual.RenderOpen()) { drawing.DrawRectangle(checker, null, new Rect(0, 0, 420, 460)); drawing.DrawImage(highlighted, new Rect(0, 0, 420, 460)); }
                    var highlightedPreview = new RenderTargetBitmap(420, 460, 96, 96, PixelFormats.Pbgra32); highlightedPreview.Render(visual);
                    var highlightedEncoder = new PngBitmapEncoder(); highlightedEncoder.Frames.Add(BitmapFrame.Create(highlightedPreview));
                    using var highlightedStream = File.Create(Path.Combine(dir, "file-orbit-highlight.png")); highlightedEncoder.Save(highlightedStream);
                }
            }
            finally { window.Close(); if (window.IsVisible) Pump(500); }
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

    [Theory]
    [InlineData("folder", 4)]
    [InlineData("video.mp4", 5)]
    [InlineData("photo.png", 7)]
    public void Reference_sectors_have_separate_hit_regions_and_details_stay_on_the_monitor(string input, int expected)
    {
        WpfStaTestHost.Run(_ =>
        {
            var window = WpfStaTestHost.KeepOffscreen(new FileOrbitWindow([input], true));
            try
            {
                var work = new Rect(-32500, -32500, 1000, 800);
                window.PlaceAt(work.BottomRight, work); window.Show(); window.UpdateLayout();
                var canvas = (Canvas)window.FindName("OrbitCanvas");
                var buttons = canvas.Children.OfType<Button>().Where(b => b.Tag is Island.Core.Files.FileAction).ToArray();
                Assert.Equal(expected, buttons.Length);
                for (int i = 0; i < buttons.Length; i++)
                {
                    var sector = Assert.IsType<Island.App.Shell.RadialSectorButton>(buttons[i]);
                    Assert.True(new Rect(0, 0, 520, 520).Contains(sector.Sector.Bounds));
                    var target = canvas.InputHitTest(Island.App.Shell.RadialSectorButton.Polar(sector.Angle, 164));
                    Assert.Same(sector, target);
                    Assert.NotSame(sector, canvas.InputHitTest(new System.Windows.Point(260, 260)));
                    for (int j = i + 1; j < buttons.Length; j++)
                    {
                        var other = Assert.IsType<Island.App.Shell.RadialSectorButton>(buttons[j]);
                        Assert.Equal(IntersectionDetail.Empty, sector.Sector.FillContainsWithDetail(other.Sector));
                    }
                }
                Rect radial = new(window.Left, window.Top, window.Width, window.Height);
                buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var detailBounds = new Rect(window.Left, window.Top, window.Width, window.Height);
                Assert.True(work.Contains(detailBounds), $"Expected {work} to contain {detailBounds}");
                ((Button)window.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(radial, new Rect(window.Left, window.Top, window.Width, window.Height));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Animated_menu_closes_after_its_short_exit_transition()
    {
        WpfStaTestHost.Run(_ =>
        {
            var window = WpfStaTestHost.KeepOffscreen(new FileOrbitWindow(["photo.png"], false));
            window.Show(); Pump(1150); window.Close();
            Assert.True(window.IsVisible);
            Pump(500);
            Assert.False(window.IsVisible);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Hover_updates_the_reference_hub_and_return_clears_the_previous_selection(bool reduce)
    {
        WpfStaTestHost.Run(_ =>
        {
            var window = WpfStaTestHost.KeepOffscreen(new FileOrbitWindow(["photo.png"], reduce));
            try
            {
                window.Show(); window.UpdateLayout(); if (!reduce) Pump(1150);
                var canvas = (Canvas)window.FindName("OrbitCanvas");
                var button = canvas.Children.OfType<Button>().Single(b => b.Tag is Island.Core.Files.FileAction.Duplicates);
                button.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
                if (!reduce) Pump(400);
                window.UpdateLayout();
                Assert.Equal("Duplicados", ((TextBlock)window.FindName("HubTitle")).Text);
                var shape = (System.Windows.Shapes.Path)((FrameworkElement)button.Content).FindName("SectorShape");
                var activeColor = ((SolidColorBrush)shape.Fill).Color;
                Assert.Equal(((byte)255, (byte)166, (byte)107, (byte)255), (activeColor.A, activeColor.R, activeColor.G, activeColor.B));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!reduce)
                {
                    button.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                    { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent });
                    Assert.Equal("Duplicados", ((TextBlock)window.FindName("HubTitle")).Text);
                    Assert.Equal(16, ((Canvas)window.FindName("FeedbackCanvas")).Children.Count);
                    Pump(400);
                }
                Assert.False(window.IsRunning);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("OptionsPanel")).Visibility);
                ((Button)window.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!reduce) Pump(1150);
                window.UpdateLayout();
                Assert.Equal("Escolha", ((TextBlock)window.FindName("HubTitle")).Text);
                Assert.Equal(System.Windows.Media.Color.FromRgb(25, 27, 32), ((SolidColorBrush)shape.Fill).Color);
            }
            finally { window.Close(); if (window.IsVisible) Pump(500); }
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

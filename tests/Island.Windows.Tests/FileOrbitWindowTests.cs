using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.App.Shell;

namespace Island.Windows.Tests;

[Collection("Calendar WPF")]
public sealed class FileOrbitWindowTests
{
    [Fact]
    public void Orbit_renders_compatible_actions_and_selection_does_not_execute()
    {
        WpfStaTestHost.Run(_ =>
        {
            var window = WpfStaTestHost.KeepOffscreen(new FileOrbitWindow(["example.png"], true));
            try
            {
                window.Show(); window.UpdateLayout();
                var canvas = (Canvas)window.FindName("OrbitCanvas");
                var buttons = canvas.Children.OfType<Button>().ToArray();
                Assert.Equal(7, buttons.Length);
                Assert.DoesNotContain(buttons, b => b.Content.ToString()!.Contains("Áudio"));
                buttons.First(b => b.Tag is Island.Core.Files.FileAction.Images).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("OptionsPanel")).Visibility);
                Assert.True(((Button)window.FindName("RunButton")).IsEnabled);
                Assert.False(window.IsRunning);
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
                    var orbit = new RenderTargetBitmap(720, 640, 96, 96, PixelFormats.Pbgra32); orbit.Render(window);
                    var second = new PngBitmapEncoder(); second.Frames.Add(BitmapFrame.Create(orbit));
                    using var other = File.Create(Path.Combine(dir, "file-orbit.png")); second.Save(other);
                }
            }
            finally { window.Close(); }
        });
    }
}

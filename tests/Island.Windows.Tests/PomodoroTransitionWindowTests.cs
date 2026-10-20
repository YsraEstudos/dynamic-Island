using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Island.Core.Pomodoro;

namespace Island.Windows.Tests;

[Collection("Calendar WPF")]
public sealed class PomodoroTransitionWindowTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Transition_renders_and_can_be_dismissed(bool rest, bool reduce)
    {
        WpfStaTestHost.Run(_ =>
        {
            Window window = Create(rest, reduce);
            WpfStaTestHost.KeepOffscreen(window);
            try
            {
                window.Show();
                PumpUntil(() => ((TextBlock)window.FindName("Headline")).Text.Length > 0, 2);
                Assert.True(window.IsVisible);
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.True(window.Topmost);
                var coffee = (FrameworkElement)window.FindName("Coffee");
                Assert.Equal(rest ? Visibility.Visible : Visibility.Collapsed, coffee.Visibility);
                PumpUntil(() => ((TextBlock)window.FindName("Headline")).Text ==
                    (rest ? "Hora de descansar" : "Hora de estudar"), 3);
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                byte[] pixels = new byte[960 * 540 * 4];
                bitmap.CopyPixels(pixels, 960 * 4, 0);
                Assert.Contains(pixels, value => value > 100);
                if (Environment.GetEnvironmentVariable("POMODORO_PREVIEW_DIR") is { Length: > 0 } previewDir)
                {
                    System.IO.Directory.CreateDirectory(previewDir);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = System.IO.File.Create(System.IO.Path.Combine(previewDir,
                        $"{(rest ? "rest" : "focus")}-{(reduce ? "still" : "animated")}.png"));
                    encoder.Save(stream);
                }

                if (reduce)
                    ((Button)window.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else
                    window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
                        PresentationSource.FromVisual(window)!, 0, Key.Escape)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Assert.False(window.IsVisible);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Transition_closes_automatically_after_six_seconds(bool reduce)
    {
        WpfStaTestHost.Run(_ =>
        {
            Window window = WpfStaTestHost.KeepOffscreen(Create(true, reduce));
            try
            {
                var elapsed = Stopwatch.StartNew();
                window.Show();
                PumpUntil(() => !window.IsVisible, 9);
                Assert.InRange(elapsed.Elapsed.TotalSeconds, 5.8, 9);
            }
            finally { window.Close(); }
        });
    }

    private static Window Create(bool rest, bool reduce)
    {
        Type? type = typeof(Island.App.App).Assembly.GetType("Island.App.Shell.PomodoroTransitionWindow");
        Assert.NotNull(type);
        var transition = new PomodoroTransition(rest ? PomodoroPhase.Focus : PomodoroPhase.Break,
            rest ? PomodoroPhase.Break : PomodoroPhase.Focus, 1, 2, false, true,
            TimeSpan.FromMinutes(rest ? 5 : 25));
        return (Window)Activator.CreateInstance(type, transition, reduce)!;
    }

    private static void PumpUntil(Func<bool> done, double seconds)
    {
        var frame = new DispatcherFrame();
        var elapsed = Stopwatch.StartNew();
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        poll.Tick += (_, _) => { if (done() || elapsed.Elapsed.TotalSeconds > seconds) frame.Continue = false; };
        poll.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { poll.Stop(); }
        Assert.True(done(), "The transition did not reach the expected state before its deadline.");
    }
}

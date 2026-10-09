using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Island.App.Views.Widgets;
using Island.App.Widgets;
using Island.Core.Capture;
using Island.Core.Configuration;
using Island.Core.Fakes;

namespace Island.Windows.Tests.Capture;

/// <summary>
/// Builds the real Capture widget on the shared STA dispatcher, so every StaticResource in its XAML is resolved.
/// A missing key would throw here instead of when the user first opens the shelf.
/// </summary>
[Collection("Calendar WPF")]
public sealed class CaptureWidgetTests
{
    [Fact]
    public void Idle_widget_offers_print_and_record_and_shows_the_empty_thumbnail()
    {
        WpfStaTestHost.Run(_ =>
        {
            var (widget, controller) = Create(new FakeShortcutStatus());
            try
            {
                widget.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, widget));

                Assert.Equal("Captura de tela", TextOf(widget, "StatusTitle"));
                Assert.Equal(Visibility.Collapsed, ElementOf(widget, "RecordDot").Visibility);
                Assert.Equal(Visibility.Collapsed, ElementOf(widget, "TimerText").Visibility);
                Assert.Equal("Gravar", ButtonOf(widget, "RecordButton").LabelText);
                Assert.Equal(Visibility.Visible, ElementOf(widget, "ThumbEmpty").Visibility);
                Assert.Equal(Visibility.Collapsed, ElementOf(widget, "ConflictPanel").Visibility);
            }
            finally
            {
                controller.Dispose();
            }
        });
    }

    [Fact]
    public void Recording_widget_shows_the_dot_timer_and_stop_label()
    {
        WpfStaTestHost.Run(_ =>
        {
            var (widget, controller) = Create(new FakeShortcutStatus());
            try
            {
                widget.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, widget));
                Assert.True(controller.StartRecordingAsync().GetAwaiter().GetResult());

                Invoke(widget, "Refresh");

                Assert.Equal("Gravando", TextOf(widget, "StatusTitle"));
                Assert.Equal(Visibility.Visible, ElementOf(widget, "RecordDot").Visibility);
                Assert.Equal(Visibility.Visible, ElementOf(widget, "TimerText").Visibility);
                Assert.Equal("Parar", ButtonOf(widget, "RecordButton").LabelText);

                controller.StopRecordingAsync().GetAwaiter().GetResult();
                Invoke(widget, "Refresh");
                Assert.Equal("Captura de tela", TextOf(widget, "StatusTitle"));
                Assert.Equal(Visibility.Collapsed, ElementOf(widget, "RecordDot").Visibility);
            }
            finally
            {
                controller.Dispose();
            }
        });
    }

    [Fact]
    public void A_taken_shortcut_is_explained_on_the_widget()
    {
        WpfStaTestHost.Run(_ =>
        {
            var shortcuts = new FakeShortcutStatus { PrintAvailable = false };
            var (widget, controller) = Create(shortcuts);
            try
            {
                widget.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, widget));

                Assert.Equal(Visibility.Visible, ElementOf(widget, "ConflictPanel").Visibility);
                Assert.Equal("Ctrl+Alt+P em uso por outro app", TextOf(widget, "ConflictText"));
            }
            finally
            {
                controller.Dispose();
            }
        });
    }

    private static (CaptureWidget Widget, CaptureController Controller) Create(ICaptureShortcutStatus shortcuts)
    {
        var fake = new FakeScreenCaptureService();
        var controller = new CaptureController(fake, Path.GetTempPath(), _ => { }, latestScreenshotPath: null, fileExists: _ => false);
        var settings = new IslandSettings { ReduceAnimations = true };
        var context = new ShelfContext(null!, null!, null!, null!, null!, new FakeClipboardService(), null!, null!, null!, null!,
            () => settings, _ => { }, capture: controller, captureShortcuts: shortcuts);
        return (new CaptureWidget(context), controller);
    }

    private static FrameworkElement ElementOf(CaptureWidget widget, string name) =>
        (FrameworkElement)widget.FindName(name);

    private static string TextOf(CaptureWidget widget, string name) =>
        Assert.IsType<TextBlock>(widget.FindName(name)).Text;

    private static ShelfButton ButtonOf(CaptureWidget widget, string name) =>
        Assert.IsType<ShelfButton>(widget.FindName(name));

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);

    private sealed class FakeShortcutStatus : ICaptureShortcutStatus
    {
        public bool PrintAvailable { get; set; } = true;

        public bool RecordAvailable { get; set; } = true;

        public event Action? AvailabilityChanged
        {
            add { }
            remove { }
        }
    }
}

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Island.App.Animations;
using Island.App.Views;
using Island.Core.Configuration;
using Island.Core.Models;
using Island.Core.Pomodoro;

namespace Island.Windows.Tests;

[Collection("Calendar WPF")]
public sealed class CalendarIndicatorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Pending_indicator_coexists_with_audio_and_timer_and_disappears_when_completed(bool vertical, bool pomodoro)
    {
        WpfStaTestHost.Run(_ =>
        {
            var shape = IslandShapeTable.For(IslandMode.Compact, new IslandSettings(), pomodoro, true, [], vertical);
            FrameworkElement view = vertical ? new VerticalCompactView() : new CompactView();
            view.DataContext = Context(pomodoro, pending: true);
            var surface = new Grid { Width = shape.Width, Height = shape.Height };
            surface.Children.Add(view);
            var window = WpfStaTestHost.KeepOffscreen(new Window { Content = surface, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false });
            try
            {
                window.Show();
                Pump();
                FrameworkElement pending = FindElements(view).Single(element => AutomationProperties.GetName(element) == "Há tarefas pendentes");
                FrameworkElement audio = FindElements(view).Single(element => AutomationProperties.GetName(element) == "Áudio em reprodução");
                Assert.Equal(Visibility.Visible, pending.Visibility);
                Assert.Equal(Visibility.Visible, audio.Visibility);
                Rect pendingBounds = Bounds(pending, surface);
                Rect audioBounds = Bounds(audio, surface);
                Assert.False(pendingBounds.IntersectsWith(audioBounds), "Task and audio glyphs overlap.");
                Assert.True(new Rect(0, 0, shape.Width, shape.Height).Contains(pendingBounds));
                if (pomodoro)
                {
                    var timer = FindElements(view).OfType<TextBlock>().Single(text => text.Text == "25:00");
                    Assert.False(Bounds(timer, surface).IntersectsWith(pendingBounds), "Task glyph overlaps the Pomodoro countdown.");
                }

                view.DataContext = Context(pomodoro, pending: false);
                Pump();
                Assert.Equal(Visibility.Collapsed, pending.Visibility);
                Assert.Equal(Visibility.Visible, audio.Visibility);
            }
            finally { window.Close(); }
        });
    }

    private static object Context(bool pomodoro, bool pending) => new
    {
        HasPendingTasks = pending,
        IsEqualizerActive = true,
        PomodoroRunning = pomodoro,
        PomodoroText = "25:00",
        PomodoroPhase = PomodoroPhase.Focus,
        PomodoroAngry = false,
        PomodoroReduceMotion = true,
    };

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static IEnumerable<FrameworkElement> FindElements(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in FindElements(child)) yield return descendant;
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Island.App.Budgets;
using Island.App.ViewModels;
using Island.App.Views;
using Island.App.Widgets;
using Island.Core.Abstractions;
using Island.Core.Application;
using Island.Core.Budgets;
using Island.Core.Calendar;
using Island.Core.Clipboard;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Idle;
using Island.Core.Models;
using Island.Core.Pomodoro;
using Island.Core.Progress;
using Island.Core.Shelf;

namespace Island.Windows.Tests;

[Collection("Calendar WPF")]
public sealed class AuditPresentationTests
{
    [Fact]
    public void Compact_timeline_ticks_keep_core_current_without_dispatching_hidden_progress()
    {
        WpfStaTestHost.Run(_ =>
        {
            var settings = new IslandSettings { IdleModeEnabled = false };
            var scheduler = new ManualScheduler();
            var media = new FakeMediaService();
            var volume = new FakeVolumeService();
            using var timer = new PomodoroTimer(scheduler, () => settings);
            using var angry = new AngryPomodoro(timer, () => "test phrase");
            using var schedule = new PomodoroSchedule(timer, angry, scheduler);
            using var weather = new WeatherRefresher(new FakeWeatherService(), new InMemoryWeatherCache(), scheduler, () => DateTimeOffset.Now);
            using var coordinator = new IslandCoordinator(media, volume, new FakeDisplayService(), scheduler, () => settings);
            coordinator.Start();
            using var idle = new IdlePresenter(coordinator, timer, schedule, weather, () => settings);
            var feed = new FakeProgressFeed();
            using var tracker = new ProgressTracker(feed, scheduler, () => DateTimeOffset.Now);
            using var progress = new ProgressPresenter(tracker, () => settings);
            var history = new ClipboardHistory(() => settings);
            using var context = new ShelfContext(timer, angry, schedule, new FileTray(), history, new FakeClipboardService(),
                media, new CalendarAgenda(new CalendarStore()), null!, null!, () => settings, _ => { });
            using var vm = new IslandViewModel(coordinator, media, volume, () => settings, context, idle, progress);
            media.SetMedia(new MediaInfo("Track", "Artist", null, false, TimeSpan.Zero, TimeSpan.FromMinutes(3), "test"));
            Drain();
            int progressNotifications = 0;
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Position)) progressNotifications++; };
            media.Tick(TimeSpan.FromSeconds(12));
            Drain();
            Assert.Equal(TimeSpan.FromSeconds(12), coordinator.State.Media!.Position);
            Assert.Equal(0, progressNotifications);
            coordinator.Post(new IslandEvent.ExpandRequested());
            Drain();
            Assert.Equal(TimeSpan.FromSeconds(12), vm.Position);

            var view = new ClipboardView();
            view.Attach(vm);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            history.Add(ClipboardItem.FromText("copied while unloaded"));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.Single(((ItemsControl)view.FindName("Cards")).Items);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        });
    }

    [Fact]
    public void Budget_toolkit_commands_keep_add_selection_remove_and_notifications()
    {
        var book = new BudgetBook(new BudgetStore());
        var vm = new BudgetsViewModel(book);
        int changes = 0;
        vm.PropertyChanged += (_, _) => changes++;
        vm.AddCommand.Execute(null);
        Assert.Equal(2, vm.Budgets.Count);
        Assert.NotNull(vm.Selected);
        vm.RemoveCommand.Execute(null);
        Assert.Single(vm.Budgets);
        Assert.True(changes > 0);
        vm.Detach();
    }

    [Fact]
    public void Progress_presenter_shows_the_active_task_and_percentage()
    {
        WpfStaTestHost.Run(_ =>
        {
            var feed = new FakeProgressFeed();
            using var tracker = new ProgressTracker(feed, new ManualScheduler(), () => DateTimeOffset.Now);
            feed.Push(new ProgressUpdate("task", "Export", "Video", ProgressState.Running, 25, null, "test"));
            using var presenter = new ProgressPresenter(tracker, () => new IslandSettings());
            Assert.True(presenter.IsActive);
            Assert.Equal(0.25, presenter.Fraction);
            Assert.Single(presenter.Tasks);
        });
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private sealed class CalendarStore : ICalendarStore
    {
        public CalendarData Load() => CalendarData.Empty;
        public void Save(CalendarData data) { }
    }
    private sealed class BudgetStore : IBudgetStore
    {
        public BudgetsData Load() => new([], null);
        public void Save(BudgetsData data) { }
    }
}

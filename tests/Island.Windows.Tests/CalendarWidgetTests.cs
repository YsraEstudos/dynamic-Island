using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Island.App.Views.Widgets;
using Island.App.Widgets;
using Island.Core.Abstractions;
using Island.Core.Calendar;
using Island.Core.Configuration;

namespace Island.Windows.Tests;

[Collection("Calendar WPF")]
public sealed class CalendarWidgetTests
{
    [Fact]
    public void Month_navigation_reaches_the_supported_date_boundaries_without_crashing()
    {
        WpfStaTestHost.Run(_ =>
        {
            var widget = CreateWidget(reduceAnimations: true);
            SetField(widget, "_shown", new DateTime(1, 2, 1));
            Invoke(widget, "ChangeMonth", -1);
            Assert.Equal(new DateTime(1, 1, 1), GetField<DateTime>(widget, "_shown"));
            Assert.Equal(42, Assert.IsType<System.Windows.Controls.Primitives.UniformGrid>(widget.FindName("DayGrid")).Children.Count);
            Invoke(widget, "ChangeMonth", -1);
            Assert.Equal(new DateTime(1, 1, 1), GetField<DateTime>(widget, "_shown"));

            SetField(widget, "_shown", new DateTime(9999, 11, 1));
            Invoke(widget, "ChangeMonth", 1);
            Assert.Equal(new DateTime(9999, 12, 1), GetField<DateTime>(widget, "_shown"));
            Assert.Equal(42, Assert.IsType<System.Windows.Controls.Primitives.UniformGrid>(widget.FindName("DayGrid")).Children.Count);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Date_rollover_updates_today_and_preserves_an_explicit_month_selection(bool followToday)
    {
        WpfStaTestHost.Run(_ =>
        {
            var widget = CreateWidget(reduceAnimations: true);
            SetField(widget, "_today", new DateTime(2026, 10, 31));
            SetField(widget, "_shown", new DateTime(2026, 10, 1));
            SetField(widget, "_followToday", followToday);

            Invoke(widget, "RefreshToday", new DateTime(2026, 11, 1));

            Assert.Equal(new DateTime(2026, 11, 1), GetField<DateTime>(widget, "_today"));
            var expectedMonth = new DateTime(2026, followToday ? 11 : 10, 1);
            Assert.Equal(expectedMonth, GetField<DateTime>(widget, "_shown"));
            var title = Assert.IsType<TextBlock>(widget.FindName("MonthTitle"));
            string monthName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(expectedMonth.Month));
            Assert.Equal($"{monthName} 2026", title.Text);
        });
    }

    [Fact]
    public void Unloading_a_calendar_stops_refresh_and_allows_navigation_after_reloading()
    {
        WpfStaTestHost.Run(_ =>
        {
            var widget = CreateWidget(reduceAnimations: false);
            var firstWindow = new Window { Content = widget, ShowInTaskbar = false, Width = 320, Height = 210 };
            Window? secondWindow = null;
            try
            {
                firstWindow.Show();
                PumpLoadedEvents();
                var timer = GetField<DispatcherTimer>(widget, "_dateTimer");
                Assert.True(timer.IsEnabled);
                Invoke(widget, "ChangeMonth", 1);
                var firstMonth = GetField<DateTime>(widget, "_shown");

                firstWindow.Content = null;
                PumpLoadedEvents();
                Assert.False(timer.IsEnabled);
                Assert.False(GetField<bool>(widget, "_isMonthTransition"));

                secondWindow = new Window { Content = widget, ShowInTaskbar = false, Width = 320, Height = 210 };
                secondWindow.Show();
                PumpLoadedEvents();
                Assert.True(timer.IsEnabled);
                Invoke(widget, "ChangeMonth", 1);
                Assert.Equal(firstMonth.AddMonths(1), GetField<DateTime>(widget, "_shown"));
            }
            finally
            {
                secondWindow?.Close();
                firstWindow.Close();
                PumpLoadedEvents();
            }
        });
    }

    private static CalendarWidget CreateWidget(bool reduceAnimations)
    {
        var settings = new IslandSettings { ReduceAnimations = reduceAnimations };
        var context = new ShelfContext(null!, null!, null!, null!, null!, null!, null!,
            new CalendarAgenda(new MemoryCalendarStore()), null!, null!, () => settings, _ => { });
        return new CalendarWidget(context);
    }

    private static void PumpLoadedEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Invoke(object target, string method, params object[] arguments) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);

    private static void SetField(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static T GetField<T>(object target, string field) =>
        (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private sealed class MemoryCalendarStore : ICalendarStore
    {
        public CalendarData Load() => CalendarData.Empty;
        public void Save(CalendarData data) { }
    }
}

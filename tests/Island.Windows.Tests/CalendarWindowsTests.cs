using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Automation;
using Island.App.Shell;
using Island.Core.Abstractions;
using Island.Core.Calendar;

namespace Island.Windows.Tests;

[Collection("Calendar WPF")]
public sealed class CalendarWindowsTests
{
    [Fact]
    public void EntryWindow_RendersContent_ForEveryKindAndAnimationMode()
    {
        WpfStaTestHost.Run(app =>
        {
            foreach ((CalendarEntryKind kind, bool reduceAnimations, string title) in new[]
            {
                (CalendarEntryKind.Task, false, "Nova tarefa"),
                (CalendarEntryKind.Event, false, "Novo evento"),
                (CalendarEntryKind.Birthday, false, "Novo aniversário"),
                (CalendarEntryKind.Task, true, "Nova tarefa"),
                (CalendarEntryKind.Event, true, "Novo evento"),
                (CalendarEntryKind.Birthday, true, "Novo aniversário"),
            })
            {
                RunWindowAndCapture(app, kind, reduceAnimations, title);
            }

            RunWindowAndCapture(
                app,
                CalendarEntryKind.Task,
                reduceAnimations: true,
                expectedTitle: "Nova tarefa",
                agenda: new CalendarAgenda(new UnavailableCalendarStore()),
                interactWindow: window =>
                {
                    var nameBox = Assert.IsType<TextBox>(window.FindName("NameBox"));
                    var saveError = Assert.IsType<TextBlock>(window.FindName("SaveError"));
                    var saveButton = Assert.IsType<Button>(window.FindName("SaveButton"));
                    Assert.Equal(Visibility.Visible, saveError.Visibility);
                    Assert.False(saveButton.IsEnabled);
                    nameBox.Text = "Tarefa de teste";
                    Assert.Equal(Visibility.Visible, saveError.Visibility);
                });
        });
    }

    [Fact]
    public void EntryWindow_SavesValidItemsAndKeepsInvalidOrFailedSubmissionsOpen()
    {
        WpfStaTestHost.Run(app =>
        {
            var taskStore = new RecordingCalendarStore();
            Assert.True(RunWindowAndCapture(app, CalendarEntryKind.Task, true, "Nova tarefa",
                new CalendarAgenda(taskStore), window =>
                {
                    Assert.IsType<TextBox>(window.FindName("NameBox")).Text = "Revisar agenda";
                    Click(window, "SaveButton");
                }));
            Assert.Equal("Revisar agenda", Assert.Single(taskStore.Data.Tasks).Title);
            Assert.Equal(new DateOnly(2026, 10, 9), taskStore.Data.Tasks[0].DueDate);

            var birthdayStore = new RecordingCalendarStore();
            Assert.True(RunWindowAndCapture(app, CalendarEntryKind.Birthday, true, "Novo aniversário",
                new CalendarAgenda(birthdayStore), window =>
                {
                    Assert.IsType<TextBox>(window.FindName("NameBox")).Text = "Ana";
                    Click(window, "SaveButton");
                }));
            CalendarBirthday birthday = Assert.Single(birthdayStore.Data.Birthdays);
            Assert.Equal("Ana", birthday.Name);
            Assert.Equal((10, 9), (birthday.Month, birthday.Day));

            foreach (TimeOnly? time in new TimeOnly?[] { new TimeOnly(14, 30), null })
            {
                var eventStore = new RecordingCalendarStore();
                Assert.True(RunWindowAndCapture(app, CalendarEntryKind.Event, true, "Novo evento",
                    new CalendarAgenda(eventStore), window =>
                    {
                        Assert.IsType<TextBox>(window.FindName("NameBox")).Text = "Consulta";
                        if (time is not null) Assert.IsType<TextBox>(window.FindName("TimeBox")).Text = "14:30";
                        Click(window, "SaveButton");
                    }));
                CalendarEvent calendarEvent = Assert.Single(eventStore.Data.Events);
                Assert.Equal(time, calendarEvent.Time);
                Assert.Equal(new DateOnly(2026, 10, 9), calendarEvent.Date);
            }

            var cancelStore = new RecordingCalendarStore();
            Assert.False(RunWindowAndCapture(app, CalendarEntryKind.Task, true, "Nova tarefa",
                new CalendarAgenda(cancelStore), window => Click(window, "CancelButton")));
            Assert.Empty(cancelStore.Data.Tasks);
            Assert.Equal(0, cancelStore.SaveCount);

            var invalidNameStore = new RecordingCalendarStore();
            Assert.False(RunWindowAndCapture(app, CalendarEntryKind.Task, true, "Nova tarefa",
                new CalendarAgenda(invalidNameStore), window =>
                {
                    Click(window, "SaveButton");
                    Assert.True(window.IsVisible);
                    Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(window.FindName("NameError")).Visibility);
                    Click(window, "CancelButton");
                }));
            Assert.Empty(invalidNameStore.Data.Tasks);

            var invalidTimeStore = new RecordingCalendarStore();
            Assert.False(RunWindowAndCapture(app, CalendarEntryKind.Event, true, "Novo evento",
                new CalendarAgenda(invalidTimeStore), window =>
                {
                    Assert.IsType<TextBox>(window.FindName("NameBox")).Text = "Consulta";
                    Assert.IsType<TextBox>(window.FindName("TimeBox")).Text = "25:99";
                    Click(window, "SaveButton");
                    Assert.True(window.IsVisible);
                    Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(window.FindName("TimeError")).Visibility);
                    Click(window, "CancelButton");
                }));
            Assert.Empty(invalidTimeStore.Data.Events);

            var failedStore = new RecordingCalendarStore { FailWrites = true };
            Assert.False(RunWindowAndCapture(app, CalendarEntryKind.Task, true, "Nova tarefa",
                new CalendarAgenda(failedStore), window =>
                {
                    Assert.IsType<TextBox>(window.FindName("NameBox")).Text = "Não salvar";
                    Click(window, "SaveButton");
                    Assert.True(window.IsVisible);
                    Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(window.FindName("SaveError")).Visibility);
                    Click(window, "CancelButton");
                }));
            Assert.Empty(failedStore.Data.Tasks);
        });
    }

    [Fact]
    public void DayWindow_ShowsMixedItemsRefreshesCompletionAndProtectsUnavailableCalendar()
    {
        WpfStaTestHost.Run(app =>
        {
            var date = new DateOnly(2026, 10, 9);
            var store = new RecordingCalendarStore(new CalendarData
            {
                Tasks = [new CalendarTask(Guid.NewGuid(), "Tarefa pendente", date, false)],
                Events = [new CalendarEvent(Guid.NewGuid(), "Consulta", date, new TimeOnly(14, 30))],
                Birthdays = [new CalendarBirthday(Guid.NewGuid(), "Ana", 10, 9)],
            });
            var agenda = new CalendarAgenda(store);
            RunDayWindow(app, agenda, date, window =>
            {
                string[] text = FindVisualChildren<TextBlock>(window).Select(block => block.Text).ToArray();
                Assert.Contains("Tarefa pendente", text);
                Assert.Contains("Consulta", text);
                Assert.Contains("Aniversário", text);
                Assert.Contains("14:30", text);

                Button complete = FindVisualChildren<Button>(window).Single(button =>
                    AutomationProperties.GetName(button) == "Concluir tarefa: Tarefa pendente");
                Click(complete);

                Assert.True(Assert.Single(store.Data.Tasks).IsCompleted);
                Assert.Contains(FindVisualChildren<Button>(window), button =>
                    AutomationProperties.GetName(button) == "Reabrir tarefa: Tarefa pendente");
                Assert.Equal(1, store.SaveCount);
            });

            RunDayWindow(app, new CalendarAgenda(new UnavailableCalendarStore()), date, window =>
            {
                string[] text = FindVisualChildren<TextBlock>(window).Select(block => block.Text).ToArray();
                Assert.Contains(text, value => value.Contains("Os dados originais foram preservados", StringComparison.Ordinal));
                Assert.False(Assert.IsType<Button>(window.FindName("AddTaskButton")).IsEnabled);
                Assert.False(Assert.IsType<Button>(window.FindName("AddEventButton")).IsEnabled);
                Assert.False(Assert.IsType<Button>(window.FindName("AddBirthdayButton")).IsEnabled);
            });
        });
    }

    private static bool? RunWindowAndCapture(
        Application app,
        CalendarEntryKind kind,
        bool reduceAnimations,
        string expectedTitle,
        CalendarAgenda? agenda = null,
        Action<Window>? interactWindow = null)
    {
        Exception? dispatcherFailure = null;
        DispatcherUnhandledExceptionEventHandler captureDispatcherFailure = (_, args) =>
        {
            dispatcherFailure = args.Exception;
            args.Handled = true;
        };
        app.DispatcherUnhandledException += captureDispatcherFailure;

        agenda ??= new CalendarAgenda(new EmptyCalendarStore());
        var constructor = typeof(CalendarEntryWindow).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(CalendarAgenda), typeof(DateOnly), typeof(CalendarEntryKind), typeof(bool)],
            modifiers: null);
        Assert.NotNull(constructor);
        var owner = new Window { Width = 240, Height = 160, ShowInTaskbar = false };
        _ = new WindowInteropHelper(owner).EnsureHandle();
        owner.Show();
        owner.UpdateLayout();
        var window = (Window)constructor!.Invoke([agenda, new DateOnly(2026, 10, 9), kind, reduceAnimations]);
        window.Owner = owner;

        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, app.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        Exception? renderFailure = null;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                window.UpdateLayout();
                var visibleTexts = FindVisualChildren<TextBlock>(window)
                    .Select(text => text.Text)
                    .ToArray();
                Assert.Contains(expectedTitle, visibleTexts);
                Assert.True(window.ActualWidth > 300 && window.ActualHeight > 160,
                    $"Unexpected entry window size: {window.ActualWidth}x{window.ActualHeight}.");

                int width = (int)Math.Ceiling(window.ActualWidth);
                int height = (int)Math.Ceiling(window.ActualHeight);
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var pixels = new byte[width * height * 4];
                bitmap.CopyPixels(pixels, width * 4, 0);
                var title = Assert.IsType<TextBlock>(window.FindName("KindTitle"));
                Rect titleBounds = title.TransformToAncestor(window).TransformBounds(
                    new Rect(new Point(), new Size(title.ActualWidth, title.ActualHeight)));
                int visiblePixelCount = 0;
                int titlePixelCount = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i + 3] > 0 && (pixels[i] > 20 || pixels[i + 1] > 20 || pixels[i + 2] > 20))
                        visiblePixelCount++;
                }
                Assert.True(visiblePixelCount > 1000,
                    $"The entry window rendered only {visiblePixelCount} non-black pixels.");
                int minX = Math.Max(0, (int)Math.Floor(titleBounds.Left));
                int maxX = Math.Min(width, (int)Math.Ceiling(titleBounds.Right));
                int minY = Math.Max(0, (int)Math.Floor(titleBounds.Top));
                int maxY = Math.Min(height, (int)Math.Ceiling(titleBounds.Bottom));
                for (int y = minY; y < maxY; y++)
                for (int x = minX; x < maxX; x++)
                {
                    int offset = (y * width + x) * 4;
                    if (pixels[offset + 3] > 0
                        && pixels[offset] > 125 && pixels[offset + 1] > 125 && pixels[offset + 2] > 125)
                        titlePixelCount++;
                }
                Assert.True(titlePixelCount > 3,
                    $"The entry title produced only {titlePixelCount} bright pixels in its visual bounds.");
                interactWindow?.Invoke(window);
            }
            catch (Exception ex) { renderFailure = ex; }
            finally
            {
                if (window.IsVisible) window.Close();
            }
        };

        timer.Start();
        try { _ = window.ShowDialog(); }
        finally { app.DispatcherUnhandledException -= captureDispatcherFailure; }
        if (owner.IsVisible) owner.Close();
        Assert.Null(dispatcherFailure);
        Assert.True(window.Opacity > 0.99, $"The visible entry window opacity was {window.Opacity}.");
        Assert.IsNotType<TranslateTransform>(window.RenderTransform);
        if (renderFailure is not null) throw renderFailure;
        return window.DialogResult;
    }

    private static void RunDayWindow(Application app, CalendarAgenda agenda, DateOnly date, Action<Window> inspect)
    {
        Exception? dispatcherFailure = null;
        DispatcherUnhandledExceptionEventHandler captureFailure = (_, args) =>
        {
            dispatcherFailure = args.Exception;
            args.Handled = true;
        };
        app.DispatcherUnhandledException += captureFailure;

        var owner = new Window { Width = 240, Height = 160, ShowInTaskbar = false };
        _ = new WindowInteropHelper(owner).EnsureHandle();
        owner.Show();
        owner.UpdateLayout();
        var constructor = typeof(CalendarDayWindow).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(CalendarAgenda), typeof(DateOnly), typeof(bool)],
            modifiers: null);
        Assert.NotNull(constructor);
        var window = (CalendarDayWindow)constructor!.Invoke([agenda, date, true]);
        window.Owner = owner;

        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, app.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        Exception? inspectFailure = null;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try { inspect(window); }
            catch (Exception ex) { inspectFailure = ex; }
            finally
            {
                window.Close();
                owner.Close();
            }
        };
        timer.Start();
        try { window.ShowDialog(); }
        finally { app.DispatcherUnhandledException -= captureFailure; }
        Assert.Null(dispatcherFailure);
        if (inspectFailure is not null) throw inspectFailure;
    }

    private static void Click(Window window, string elementName) =>
        Assert.IsType<Button>(window.FindName(elementName)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (T descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private sealed class EmptyCalendarStore : ICalendarStore
    {
        public CalendarData Load() => CalendarData.Empty;
        public void Save(CalendarData data) { }
    }

    private sealed class RecordingCalendarStore : ICalendarStore
    {
        public RecordingCalendarStore(CalendarData? initialData = null) => Data = initialData ?? CalendarData.Empty;

        public CalendarData Data { get; private set; }
        public int SaveCount { get; private set; }
        public bool FailWrites { get; init; }
        public CalendarData Load() => Data;

        public void Save(CalendarData data)
        {
            SaveCount++;
            if (FailWrites) throw new IOException("Test storage failure.");
            Data = data;
        }
    }

    private sealed class UnavailableCalendarStore : ICalendarStore
    {
        public CalendarData Load() => throw new IOException("Test storage failure.");
        public void Save(CalendarData data) => throw new IOException("Test storage failure.");
    }
}

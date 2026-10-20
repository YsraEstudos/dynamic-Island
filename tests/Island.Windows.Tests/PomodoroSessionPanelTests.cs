using System.Windows;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Pomodoro;
using Island.App.ViewModels;
using Island.App.Shell;
using Island.App.Widgets;
using Island.App.Views.Widgets;
using Island.Core.Abstractions;
using Island.Core.Calendar;
using Island.Core.Clipboard;
using Island.Core.Shelf;
using Island.Windows.Configuration;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Island.Windows.Tests;

[Collection("Calendar WPF")]
public sealed class PomodoroSessionPanelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Session_panel_opens_with_saved_profiles_without_starting_the_timer(bool reduce)
    {
        WpfStaTestHost.Run(_ =>
        {
            var settings = new IslandSettings { PomodoroGoal = "Revisar matemática" };
            var scheduler = new ManualScheduler();
            using var timer = new PomodoroTimer(scheduler, () => settings);
            using var angry = new AngryPomodoro(timer);
            var assembly = typeof(Island.App.App).Assembly;
            var vmType = assembly.GetType("Island.App.ViewModels.PomodoroSessionViewModel");
            Assert.NotNull(vmType);
            var vm = Activator.CreateInstance(vmType, timer, angry, (Func<IslandSettings>)(() => settings),
                (Action<IslandSettings>)(s => settings = s));
            var windowType = assembly.GetType("Island.App.Shell.PomodoroSessionWindow");
            Assert.NotNull(windowType);
            var window = WpfStaTestHost.KeepOffscreen((Window)Activator.CreateInstance(windowType, vm, reduce)!);
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.NotNull(window.FindName("GoalBox"));
                Assert.NotNull(window.FindName("ProfilePicker"));
                Assert.Contains("Estudo · 25/5", VisualTexts((DependencyObject)window.FindName("ProfilePicker")));
                Capture(window, reduce ? "panel-still" : "panel-animated");
                var model = (PomodoroSessionViewModel)vm!;
                ((ComboBox)window.FindName("ProfilePicker")).SelectedItem = model.Profiles[1];
                Drain();
                Assert.Equal(50, settings.PomodoroFocusMinutes);
                Assert.Equal(TimeSpan.FromMinutes(50), timer.Remaining);
                Assert.Equal(PomodoroSoundKind.Piano, settings.PomodoroFocusSound);
                Assert.False(timer.IsRunning);
            }
            finally { window.Close(); (vm as IDisposable)?.Dispose(); }
        });
    }

    [Fact]
    public void Profile_crud_saves_the_selected_values_and_rejects_invalid_edits()
    {
        using var rig = new Rig();
        var model = rig.Model();
        model.NewProfileCommand.Execute(null);
        model.ProfileName = "Concursos"; model.FocusMinutes = "35"; model.BreakMinutes = "8";
        model.FocusSound = PomodoroSoundKind.Digital; model.BreakSound = PomodoroSoundKind.Piano;
        model.AutoAdvance = false;
        model.Goal = "Revisar matemática";
        model.SaveProfileCommand.Execute(null);
        Assert.Equal(4, rig.Settings.PomodoroProfiles.Count);
        Assert.Equal("Concursos", model.SelectedProfile!.Name);
        Assert.Equal(35, rig.Settings.PomodoroFocusMinutes);
        Assert.Equal(8, rig.Settings.PomodoroBreakMinutes);
        Assert.Equal(PomodoroSoundKind.Digital, rig.Settings.PomodoroFocusSound);
        Assert.Equal(PomodoroSoundKind.Piano, rig.Settings.PomodoroBreakSound);
        Assert.False(rig.Settings.PomodoroAutoAdvance);
        Assert.False(rig.Timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(35), rig.Timer.Remaining);
        var before = rig.Settings;
        model.FocusMinutes = "0";
        model.SaveProfileCommand.Execute(null);
        Assert.Equal(before, rig.Settings);
        Assert.Contains("120", model.Status);
        model.DeleteProfileCommand.Execute(null);
        Assert.Equal(3, rig.Settings.PomodoroProfiles.Count);
        Assert.DoesNotContain(rig.Settings.PomodoroProfiles, p => p.Name == "Concursos");
        Assert.Equal("Revisar matemática", rig.Settings.PomodoroGoal);
    }

    [Fact]
    public void Unsaved_profile_edits_do_not_change_the_next_session()
    {
        using var rig = new Rig();
        var model = rig.Model();
        var before = rig.Settings;
        model.FocusMinutes = "35";
        model.FocusSound = PomodoroSoundKind.Digital;
        model.AutoAdvance = false;
        Assert.Equal(before, rig.Settings);
        model.SaveProfileCommand.Execute(null);
        Assert.Equal(35, rig.Settings.PomodoroFocusMinutes);
        Assert.False(rig.Settings.PomodoroAutoAdvance);
        Assert.Equal(PomodoroSoundKind.Digital, rig.Settings.PomodoroFocusSound);
        Assert.False(rig.Timer.IsRunning);
    }

    [Fact]
    public void Profiles_cannot_change_a_running_or_waiting_session()
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { PomodoroAutoAdvance = false, PomodoroFocusMinutes = 1, PomodoroBreakMinutes = 1 };
        var model = rig.Model();
        rig.Timer.StartPlan(2);
        foreach (bool waiting in new[] { false, true })
        {
            if (waiting) rig.Scheduler.Advance(TimeSpan.FromMinutes(1));
            var before = rig.Settings;
            Assert.False(model.CanConfigure);
            model.SelectedProfile = model.Profiles[1];
            model.Goal = "Must not replace the session";
            model.SaveProfileCommand.Execute(null);
            Assert.Equal(before, rig.Settings);
            Assert.True(rig.Timer.PlanActive);
            Assert.Equal(waiting ? PomodoroPhase.Break : PomodoroPhase.Focus, rig.Timer.Phase);
        }
    }

    [Fact]
    public void Profile_and_goal_round_trip_and_corrupt_profile_fields_are_normalized()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "island-profiles-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new IslandSettings { PomodoroGoal = "Revisar matemática", PomodoroAutoAdvance = false,
                PomodoroProfiles = [new PomodoroProfile { Id = "custom", Name = "Concursos", FocusMinutes = 35, BreakMinutes = 8,
                    FocusSound = PomodoroSoundKind.Piano, AutoAdvance = false }], PomodoroProfileId = "custom", PomodoroCycles = 7 };
            var store = new JsonSettingsStore(directory);
            store.Save(settings);
            Assert.Equal(settings, new JsonSettingsStore(directory).Load());
            store.Save(settings with { PomodoroProfiles = [new PomodoroProfile { Id = "same", FocusMinutes = -1 },
                new PomodoroProfile { Id = "same", BreakMinutes = 1000, FocusSound = (PomodoroSoundKind)500 }],
                PomodoroGoal = new string('a', 300), PomodoroProfileId = "missing" });
            var normalized = new JsonSettingsStore(directory).Load();
            Assert.Equal(2, normalized.PomodoroProfiles.Select(p => p.Id).Distinct().Count());
            Assert.Equal(1, normalized.PomodoroProfiles[0].FocusMinutes);
            Assert.Equal(120, normalized.PomodoroProfiles[1].BreakMinutes);
            Assert.Equal(PomodoroSoundKind.SoftBell, normalized.PomodoroProfiles[1].FocusSound);
            Assert.Equal(160, normalized.PomodoroGoal.Length);
            Assert.Equal(7, normalized.PomodoroCycles);
            Assert.Contains(normalized.PomodoroProfiles, p => p.Id == normalized.PomodoroProfileId);
        }
        finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
    }

    [Fact]
    public void Widget_shows_goal_then_warning_then_waiting_without_growing_the_shelf()
    {
        WpfStaTestHost.Run(_ =>
        {
            using var rig = new Rig();
            rig.Settings = rig.Settings with { PomodoroGoal = "Revisar matemática", PomodoroFocusMinutes = 1,
                PomodoroBreakMinutes = 1, PomodoroAutoAdvance = false, ReduceAnimations = true };
            using var context = new ShelfContext(rig.Timer, rig.Angry, rig.Schedule, new FileTray(), new ClipboardHistory(() => rig.Settings),
                new FakeClipboardService(), new FakeMediaService(), new CalendarAgenda(new CalendarStore()), null!, null!,
                () => rig.Settings, next => rig.Settings = next);
            var widget = new PomodoroWidget(context);
            var window = WpfStaTestHost.KeepOffscreen(new Window { Content = widget, Width = 320, Height = 195, Background = new SolidColorBrush(Color.FromRgb(20, 20, 23)) });
            try
            {
                window.Show(); rig.Timer.StartPlan(2); Drain();
                Assert.Equal("Revisar matemática", ((TextBlock)widget.FindName("SessionGoalText")).Text);
                Assert.Equal(152, widget.Height);
                Capture(window, "widget-goal");
                rig.Scheduler.Advance(TimeSpan.FromSeconds(30)); Drain();
                Assert.Contains("Conclua", ((TextBlock)widget.FindName("SessionGoalText")).Text);
                Capture(window, "widget-warning");
                rig.Scheduler.Advance(TimeSpan.FromSeconds(30)); Drain();
                Assert.Equal("AGUARDANDO INICIAR", ((TextBlock)widget.FindName("SessionProfileText")).Text);
                Assert.False(rig.Timer.IsRunning);
                Capture(window, "widget-waiting");
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Return_screen_shows_goal_and_only_Start_advances_the_waiting_phase(bool clickStart)
    {
        WpfStaTestHost.Run(_ =>
        {
            using var rig = new Rig();
            rig.Settings = rig.Settings with { PomodoroGoal = "Revisar matemática", PomodoroFocusMinutes = 1, PomodoroBreakMinutes = 1, PomodoroAutoAdvance = false };
            PomodoroTransition? boundary = null;
            rig.Timer.Transitioned += t => boundary = t;
            rig.Timer.StartPlan(2); rig.Scheduler.Advance(TimeSpan.FromMinutes(1));
            rig.Timer.Start(); rig.Scheduler.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(PomodoroPhase.Focus, rig.Timer.Phase);
            var window = WpfStaTestHost.KeepOffscreen(new PomodoroTransitionWindow(boundary!, true, rig.Timer.SessionGoal, rig.Timer.Start));
            try
            {
                window.Show(); Drain();
                Assert.Equal("Revisar matemática", ((TextBlock)window.FindName("SessionGoalText")).Text);
                Assert.True(((Button)window.FindName("StartNextButton")).IsVisible);
                Capture(window, "return-manual");
                if (clickStart) ((Button)window.FindName("StartNextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Assert.Equal(clickStart, rig.Timer.IsRunning);
                Assert.False(window.IsVisible);
            }
            finally { window.Close(); }
        });
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<string> VisualTexts(DependencyObject parent)
    {
        if (parent is TextBlock text) yield return text.Text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (string value in VisualTexts(VisualTreeHelper.GetChild(parent, i))) yield return value;
    }
    private static void Capture(Window window, string name)
    {
        var frame = new DispatcherFrame();
        var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        delay.Tick += (_, _) => { delay.Stop(); frame.Continue = false; };
        delay.Start(); Dispatcher.PushFrame(frame); window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        byte[] pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        Assert.Contains(pixels, value => value > 150);
        if (Environment.GetEnvironmentVariable("POMODORO_FEATURE_PREVIEW_DIR") is not { Length: > 0 } directory) return;
        System.IO.Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = System.IO.File.Create(System.IO.Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }

    private sealed class Rig : IDisposable
    {
        public IslandSettings Settings = new();
        public readonly ManualScheduler Scheduler = new();
        public readonly PomodoroTimer Timer;
        public readonly AngryPomodoro Angry;
        public readonly PomodoroSchedule Schedule;
        public Rig()
        {
            Timer = new PomodoroTimer(Scheduler, () => Settings, () => DateTimeOffset.UnixEpoch + Scheduler.Now);
            Angry = new AngryPomodoro(Timer);
            Schedule = new PomodoroSchedule(Timer, Angry, Scheduler, () => DateTimeOffset.UnixEpoch + Scheduler.Now);
        }
        public PomodoroSessionViewModel Model() => new(Timer, Angry, () => Settings, s => Settings = s);
        public void Dispose() { Schedule.Dispose(); Angry.Dispose(); Timer.Dispose(); }
    }
    private sealed class CalendarStore : ICalendarStore
    {
        public CalendarData Load() => CalendarData.Empty;
        public void Save(CalendarData data) { }
    }
}

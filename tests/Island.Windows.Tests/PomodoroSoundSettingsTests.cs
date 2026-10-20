using System.Windows;
using System.Windows.Controls;
using Island.App.Shell;
using Island.App.ViewModels;
using Island.Core.Configuration;
using Island.Core.Pomodoro;
using Island.Windows.Configuration;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Island.Windows.Tests;

[Collection("Calendar WPF")]
public sealed class PomodoroSoundSettingsTests : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "island-pomodoro-sounds-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_directory)) System.IO.Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Settings_offers_four_sounds_for_each_transition()
    {
        WpfStaTestHost.Run(_ =>
        {
            var vm = new SettingsViewModel(new IslandSettings(), ["Monitor 1"], _ => { });
            var window = WpfStaTestHost.KeepOffscreen(new SettingsWindow(vm));
            try
            {
                window.Show();
                window.UpdateLayout();
                var rest = Assert.IsType<ComboBox>(window.FindName("PomodoroBreakSoundPicker"));
                var focus = Assert.IsType<ComboBox>(window.FindName("PomodoroFocusSoundPicker"));
                Assert.Equal(4, rest.Items.Count);
                Assert.Equal(4, focus.Items.Count);
                Assert.NotNull(window.FindName("PreviewPomodoroBreakSoundButton"));
                Assert.NotNull(window.FindName("PreviewPomodoroFocusSoundButton"));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Selection_persists_each_cue_without_playing_and_preview_does_not_unmute()
    {
        WpfStaTestHost.Run(_ =>
        {
            var store = new JsonSettingsStore(_directory);
            var current = new IslandSettings { PomodoroSound = false };
            var previews = new List<PomodoroSoundKind>();
            var vm = new SettingsViewModel(current, ["Monitor 1"], next => { current = next; store.Save(next); },
                getCurrent: () => current, previewPomodoroSound: kind => { previews.Add(kind); return true; });
            var window = WpfStaTestHost.KeepOffscreen(new SettingsWindow(vm));
            try
            {
                window.Show();
                current = current with { PomodoroCycles = 7, ShelfWidgets = ["calendar"] };
                ((ComboBox)window.FindName("PomodoroBreakSoundPicker")).SelectedValue = PomodoroSoundKind.Piano;
                ((ComboBox)window.FindName("PomodoroFocusSoundPicker")).SelectedValue = PomodoroSoundKind.Digital;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                Assert.Empty(previews);
                var loaded = new JsonSettingsStore(_directory).Load();
                Assert.Equal(PomodoroSoundKind.Piano, loaded.PomodoroBreakSound);
                Assert.Equal(PomodoroSoundKind.Digital, loaded.PomodoroFocusSound);
                Assert.Equal(7, loaded.PomodoroCycles);
                Assert.Equal(["calendar"], loaded.ShelfWidgets);
                Assert.False(loaded.PomodoroSound);

                vm.PreviewPomodoroBreakSoundCommand.Execute(null);
                vm.PreviewPomodoroFocusSoundCommand.Execute(null);
                Assert.Equal([PomodoroSoundKind.Piano, PomodoroSoundKind.Digital], previews);
                Assert.False(current.PomodoroSound);
                var reopened = new SettingsViewModel(loaded, ["Monitor 1"], _ => { });
                Assert.Equal(PomodoroSoundKind.Piano, reopened.PomodoroBreakSound);
                Assert.Equal(PomodoroSoundKind.Digital, reopened.PomodoroFocusSound);

                var card = (FrameworkElement)window.FindName("PomodoroSoundCard");
                card.BringIntoView();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                Assert.Contains(pixels, pixel => pixel > 150);
                if (Environment.GetEnvironmentVariable("POMODORO_SOUND_PREVIEW_DIR") is { Length: > 0 } previewDir)
                {
                    System.IO.Directory.CreateDirectory(previewDir);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = System.IO.File.Create(System.IO.Path.Combine(previewDir, "settings-sounds.png"));
                    encoder.Save(file);
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Old_files_get_default_cues_and_unknown_values_fall_back_without_resetting_other_settings()
    {
        System.IO.Directory.CreateDirectory(_directory);
        string path = System.IO.Path.Combine(_directory, "settings.json");
        System.IO.File.WriteAllText(path, """{"pomodoroSound":false,"pomodoroCycles":4}""");
        var old = new JsonSettingsStore(_directory).Load();
        Assert.Equal(PomodoroSoundKind.Coffee, old.PomodoroBreakSound);
        Assert.Equal(PomodoroSoundKind.SoftBell, old.PomodoroFocusSound);
        Assert.False(old.PomodoroSound);
        System.IO.File.WriteAllText(path, """{"pomodoroBreakSound":999,"pomodoroFocusSound":-3,"pomodoroCycles":4}""");
        var invalid = new JsonSettingsStore(_directory).Load();
        Assert.Equal(PomodoroSoundKind.Coffee, invalid.PomodoroBreakSound);
        Assert.Equal(PomodoroSoundKind.SoftBell, invalid.PomodoroFocusSound);
        Assert.Equal(4, invalid.PomodoroCycles);
    }

    [Fact]
    public void Sound_changes_are_detected_by_settings_equality_and_reset_only_managed_preferences()
    {
        var settings = new IslandSettings { PomodoroFocusSound = PomodoroSoundKind.Piano,
            PomodoroBreakSound = PomodoroSoundKind.Digital, PomodoroSound = false,
            PomodoroCycles = 6, PomodoroFocusMinutes = 42, PhoneFcmToken = "keep" };
        Assert.NotEqual(settings, settings with { PomodoroFocusSound = PomodoroSoundKind.Coffee });
        Assert.NotEqual(settings, settings with { PomodoroBreakSound = PomodoroSoundKind.Coffee });
        var vm = new SettingsViewModel(settings, ["Monitor 1"], _ => { });
        vm.ResetToDefaultsCommand.Execute(null);
        Assert.True(vm.Current.PomodoroSound);
        Assert.Equal(PomodoroSoundKind.Coffee, vm.Current.PomodoroBreakSound);
        Assert.Equal(PomodoroSoundKind.SoftBell, vm.Current.PomodoroFocusSound);
        Assert.Equal(6, vm.Current.PomodoroCycles);
        Assert.Equal(42, vm.Current.PomodoroFocusMinutes);
        Assert.Equal("keep", vm.Current.PhoneFcmToken);
    }

    [Fact]
    public void Failed_preview_shows_error_without_changing_preferences()
    {
        var vm = new SettingsViewModel(new IslandSettings(), ["Monitor 1"], _ => throw new Exception("Preview must not save"),
            previewPomodoroSound: _ => false);
        vm.PreviewPomodoroFocusSoundCommand.Execute(null);
        Assert.Contains("Não foi possível", vm.SoundPreviewStatus);
    }
}

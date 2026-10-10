using System.Text;
using Island.Core.Configuration;
using Island.Windows.Configuration;

namespace Island.Windows.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "island-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var original = new IslandSettings
        {
            MonitorIndex = 2,
            CompactWidth = 200,
            CompactHeight = 44,
            TopMargin = 12,
            VolumeDisplaySeconds = 2.5,
            MediaPreviewSeconds = 4.0,
            HideInFullscreen = false,
            ReduceAnimations = true,
            StartWithWindows = true,
            ShowVolume = false,
            ShowMedia = false,
            QuickNotesHotkeyEnabled = false,
        };

        new JsonSettingsStore(_dir).Save(original);
        var loaded = new JsonSettingsStore(_dir).Load();

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void Shelf_rows_round_trip_through_the_file()
    {
        var original = new IslandSettings
        {
            ShelfWidgets = new[] { "nowplaying", "calendar", "pomodoro" },
            ShelfRows = new[] { 0, 1, 1 },
        };

        new JsonSettingsStore(_dir).Save(original);
        var loaded = new JsonSettingsStore(_dir).Load();

        Assert.Equal(original, loaded);
        Assert.Equal(new[] { new[] { "nowplaying" }, new[] { "calendar", "pomodoro" } },
            loaded.GetShelfRows().Select(r => r.ToArray()));
    }

    [Fact]
    public void Shelf_file_without_rows_loads_as_one_row()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ \"shelfWidgets\": [\"filetray\", \"pomodoro\"] }");

        var loaded = new JsonSettingsStore(_dir).Load();

        Assert.Equal(new[] { new[] { "filetray", "pomodoro" } }, loaded.GetShelfRows().Select(r => r.ToArray()));
    }

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var loaded = new JsonSettingsStore(_dir).Load();

        Assert.Equal(new IslandSettings(), loaded);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Settings_file_without_notes_hotkey_uses_enabled_default()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ \"compactWidth\": 180 }");

        var loaded = new JsonSettingsStore(_dir).Load();

        Assert.True(loaded.QuickNotesHotkeyEnabled);
    }

    [Fact]
    public void CorruptFile_ReturnsDefaults_AndIsQuarantined()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ this is not json");

        var loaded = new JsonSettingsStore(_dir).Load();

        Assert.Equal(new IslandSettings(), loaded);
        Assert.False(File.Exists(SettingsPath));
        Assert.True(File.Exists(SettingsPath + ".bad"));
    }

    [Fact]
    public void Save_LeavesNoTempFile_AndOverwritesExisting()
    {
        var store = new JsonSettingsStore(_dir);
        store.Save(new IslandSettings { CompactWidth = 150 });
        store.Save(new IslandSettings { CompactWidth = 170 });

        Assert.False(File.Exists(SettingsPath + ".tmp"));
        Assert.Equal(170, new JsonSettingsStore(_dir).Load().CompactWidth);
    }

    [Fact]
    public void Load_ClampsOutOfRangeValues()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, """
            {
              "monitorIndex": -3,
              "compactWidth": -5,
              "compactHeight": 99999,
              "topMargin": -1,
              "volumeDisplaySeconds": 0,
              "mediaPreviewSeconds": -2
            }
            """);

        var loaded = new JsonSettingsStore(_dir).Load();

        Assert.Equal(0, loaded.MonitorIndex);
        Assert.Equal(40, loaded.CompactWidth);
        Assert.Equal(400, loaded.CompactHeight);
        Assert.Equal(0, loaded.TopMargin);
        Assert.True(loaded.VolumeDisplaySeconds > 0);
        Assert.True(loaded.MediaPreviewSeconds > 0);
    }

    [Fact]
    public void Load_TolerantOfUnknownAndMissingProperties()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, """{ "compactWidth": 180, "someFutureSetting": [1, 2, 3] }""");

        var loaded = new JsonSettingsStore(_dir).Load();

        Assert.Equal(180, loaded.CompactWidth);
        Assert.Equal(new IslandSettings().CompactHeight, loaded.CompactHeight);
        Assert.Equal(new IslandSettings().ShowMedia, loaded.ShowMedia);
    }

    [Fact]
    public void Save_WritesCamelCaseIndentedJson()
    {
        new JsonSettingsStore(_dir).Save(new IslandSettings());

        string text = File.ReadAllText(SettingsPath, Encoding.UTF8);
        Assert.Contains("\"compactWidth\"", text);
        Assert.Contains("\n", text);
    }
}

public class JsonSettingsStorePhoneTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "island-phone-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Phone_settings_survive_a_save_and_load()
    {
        var store = new Island.Windows.Configuration.JsonSettingsStore(_dir);
        store.Save(new Island.Core.Configuration.IslandSettings { PhoneBlockEnabled = true, PhoneFcmToken = "tok:123" });

        var loaded = store.Load();

        Assert.True(loaded.PhoneBlockEnabled);
        Assert.Equal("tok:123", loaded.PhoneFcmToken);
    }

    [Fact]
    public void A_settings_file_from_before_the_phone_fields_loads_with_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ \"pomodoroFocusMinutes\": 40 }");

        var loaded = new Island.Windows.Configuration.JsonSettingsStore(_dir).Load();

        Assert.Equal(40, loaded.PomodoroFocusMinutes);
        Assert.False(loaded.PhoneBlockEnabled);
        Assert.Equal(string.Empty, loaded.PhoneFcmToken);
    }

    [Fact]
    public void A_settings_file_from_before_the_update_repository_uses_the_default()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ \"pomodoroFocusMinutes\": 40 }");

        var loaded = new Island.Windows.Configuration.JsonSettingsStore(_dir).Load();

        Assert.Equal("YsraEstudos/dynamic-Island", loaded.UpdateRepository);
    }

    [Fact]
    public void Update_repository_is_trimmed_and_a_blank_value_falls_back_to_the_default()
    {
        var store = new Island.Windows.Configuration.JsonSettingsStore(_dir);
        store.Save(new Island.Core.Configuration.IslandSettings { UpdateRepository = "  someone/fork  " });
        Assert.Equal("someone/fork", store.Load().UpdateRepository);

        store.Save(new Island.Core.Configuration.IslandSettings { UpdateRepository = "   " });
        Assert.Equal("YsraEstudos/dynamic-Island", store.Load().UpdateRepository);
    }
}

using Island.Core.Configuration;
using Island.Windows.Configuration;

namespace Island.Windows.Tests;

public class IdleSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DynamicIslandIdleSettings", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Idle_mode_and_city_roundtrip()
    {
        new JsonSettingsStore(_dir).Save(new IslandSettings { IdleModeEnabled = false, WeatherCity = "Recife" });

        IslandSettings loaded = new JsonSettingsStore(_dir).Load();

        Assert.False(loaded.IdleModeEnabled);
        Assert.Equal("Recife", loaded.WeatherCity);
    }

    [Fact]
    public void A_settings_file_from_before_idle_mode_gets_the_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ \"compactWidth\": 140 }");

        IslandSettings loaded = new JsonSettingsStore(_dir).Load();

        Assert.True(loaded.IdleModeEnabled);
        Assert.Equal(string.Empty, loaded.WeatherCity);
    }

    [Fact]
    public void The_city_is_trimmed_and_capped_and_a_null_city_becomes_empty()
    {
        var store = new JsonSettingsStore(_dir);

        store.Save(new IslandSettings { WeatherCity = "  Natal  " });
        Assert.Equal("Natal", store.Load().WeatherCity);

        store.Save(new IslandSettings { WeatherCity = new string('x', 300) });
        Assert.Equal(80, store.Load().WeatherCity.Length);

        store.Save(new IslandSettings { WeatherCity = null! });
        Assert.Equal(string.Empty, store.Load().WeatherCity);
    }
}

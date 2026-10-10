using Island.Core.Configuration;

namespace Island.Core.Tests.Idle;

public class IdleSettingsTests
{
    [Fact]
    public void Idle_settings_compare_by_value()
    {
        var a = new IslandSettings { IdleModeEnabled = false, WeatherCity = "Recife" };

        Assert.Equal(a, new IslandSettings { IdleModeEnabled = false, WeatherCity = "Recife" });
        Assert.Equal(a.GetHashCode(), new IslandSettings { IdleModeEnabled = false, WeatherCity = "Recife" }.GetHashCode());
    }

    [Fact]
    public void Changing_either_idle_setting_makes_the_settings_differ()
    {
        var a = new IslandSettings { IdleModeEnabled = true, WeatherCity = "Recife" };

        Assert.NotEqual(a, a with { IdleModeEnabled = false });
        Assert.NotEqual(a, a with { WeatherCity = "Natal" });
    }
}

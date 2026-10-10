using Island.Core.Idle;
using Island.Windows.Weather;

namespace Island.Windows.Tests.Weather;

public class JsonWeatherCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DynamicIslandWeatherTests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "weather.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Save_then_Load_roundtrips_the_reading_and_the_place()
    {
        var data = new WeatherCacheData(
            new WeatherSnapshot(23.4, 3, 40, new DateTimeOffset(2026, 10, 10, 14, 15, 0, TimeSpan.FromHours(-3))),
            new WeatherPlace("Recife", -8.05, -34.9));

        new JsonWeatherCache(_dir).Save(data);

        Assert.Equal(data, new JsonWeatherCache(_dir).Load());
    }

    [Fact]
    public void A_missing_file_loads_as_empty()
    {
        Assert.Equal(WeatherCacheData.Empty, new JsonWeatherCache(_dir).Load());
    }

    [Fact]
    public void A_corrupt_file_loads_as_empty_instead_of_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");

        Assert.Equal(WeatherCacheData.Empty, new JsonWeatherCache(_dir).Load());
    }

    [Fact]
    public void Save_leaves_no_temp_file_behind()
    {
        new JsonWeatherCache(_dir).Save(WeatherCacheData.Empty);

        Assert.True(File.Exists(FilePath));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }
}

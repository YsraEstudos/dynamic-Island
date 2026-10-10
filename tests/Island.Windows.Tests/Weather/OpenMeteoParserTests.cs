using System.Text;
using Island.Core.Idle;
using Island.Windows.Weather;

namespace Island.Windows.Tests.Weather;

public class OpenMeteoParserTests
{
    private static readonly DateTimeOffset FetchedAt = new(2026, 10, 10, 14, 15, 0, TimeSpan.FromHours(-3));

    /// <summary>A forecast payload shaped like Open-Meteo's, for a given hour, with one rain chance per hour (24 values).</summary>
    private static string Forecast(string currentTime, double temperature, int code, params int[] chances)
    {
        var times = new StringBuilder();
        for (int h = 0; h < 24; h++)
        {
            if (h > 0) times.Append(',');
            times.Append($"\"2026-10-10T{h:00}:00\"");
        }

        var values = string.Join(',', chances.Length == 24 ? chances : Enumerable.Repeat(0, 24).ToArray());
        return "{\"latitude\":-23.5,\"longitude\":-46.625,\"timezone\":\"America/Sao_Paulo\","
               + $"\"current\":{{\"time\":\"{currentTime}\",\"interval\":900,\"temperature_2m\":{temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"weather_code\":{code}}},"
               + $"\"hourly\":{{\"time\":[{times}],\"precipitation_probability\":[{values}]}}}}";
    }

    private static int[] Chances(params (int Hour, int Value)[] set)
    {
        var values = new int[24];
        foreach (var (hour, value) in set) values[hour] = value;
        return values;
    }

    [Fact]
    public void Reads_the_current_temperature_and_code()
    {
        string json = Forecast("2026-10-10T14:15", 23.4, 3, Chances((14, 10)));

        WeatherSnapshot? snapshot = OpenMeteoParser.ParseForecast(json, FetchedAt);

        Assert.NotNull(snapshot);
        Assert.Equal(23.4, snapshot.TemperatureC, precision: 6);
        Assert.Equal(3, snapshot.WeatherCode);
        Assert.Equal(FetchedAt, snapshot.FetchedAt);
    }

    [Fact]
    public void Rain_chance_is_the_highest_of_the_current_hour_and_the_next_two()
    {
        string json = Forecast("2026-10-10T14:15", 20, 61, Chances((13, 90), (14, 20), (15, 55), (16, 30), (17, 99)));

        Assert.Equal(55, OpenMeteoParser.ParseForecast(json, FetchedAt)!.RainChancePercent);
    }

    [Fact]
    public void Rain_chance_ignores_the_hour_before_and_the_hour_after_the_window()
    {
        string json = Forecast("2026-10-10T14:00", 20, 3, Chances((13, 100), (14, 5), (15, 5), (16, 5), (17, 100)));

        Assert.Equal(5, OpenMeteoParser.ParseForecast(json, FetchedAt)!.RainChancePercent);
    }

    [Fact]
    public void The_window_is_cut_short_at_the_end_of_the_forecast_day()
    {
        string json = Forecast("2026-10-10T23:30", 18, 1, Chances((23, 35)));

        Assert.Equal(35, OpenMeteoParser.ParseForecast(json, FetchedAt)!.RainChancePercent);
    }

    [Fact]
    public void Missing_hourly_values_are_skipped_not_read_as_zero()
    {
        string json = "{\"current\":{\"time\":\"2026-10-10T14:15\",\"temperature_2m\":19,\"weather_code\":2},"
                      + "\"hourly\":{\"time\":[\"2026-10-10T14:00\",\"2026-10-10T15:00\"],\"precipitation_probability\":[null,42]}}";

        Assert.Equal(42, OpenMeteoParser.ParseForecast(json, FetchedAt)!.RainChancePercent);
    }

    [Fact]
    public void A_current_hour_missing_from_the_hourly_data_is_a_failed_read()
    {
        string json = Forecast("2026-10-11T14:15", 20, 3, Chances((14, 10)));

        Assert.Null(OpenMeteoParser.ParseForecast(json, FetchedAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"current\":{\"time\":\"2026-10-10T14:15\"}}")]
    [InlineData("{\"current\":{\"time\":\"2026-10-10T14:15\",\"temperature_2m\":\"hot\",\"weather_code\":3},\"hourly\":{\"time\":[],\"precipitation_probability\":[]}}")]
    public void Malformed_or_incomplete_replies_are_failed_reads(string json)
    {
        Assert.Null(OpenMeteoParser.ParseForecast(json, FetchedAt));
    }

    [Fact]
    public void A_city_is_geocoded_to_its_first_match()
    {
        const string json = "{\"results\":[{\"name\":\"Recife\",\"latitude\":-8.05,\"longitude\":-34.9},{\"name\":\"Recife\",\"latitude\":1,\"longitude\":2}]}";

        WeatherPlace? place = OpenMeteoParser.ParseGeocoding(json, "Recife");

        Assert.Equal(new WeatherPlace("Recife", -8.05, -34.9), place);
    }

    [Fact]
    public void A_city_with_no_match_is_a_failed_read()
    {
        Assert.Null(OpenMeteoParser.ParseGeocoding("{\"generationtime_ms\":0.2}", "Nowhere"));
        Assert.Null(OpenMeteoParser.ParseGeocoding("{\"results\":[]}", "Nowhere"));
    }

    [Fact]
    public void The_IP_location_is_used_only_when_the_service_says_success()
    {
        const string ok = "{\"success\":true,\"latitude\":-23.55,\"longitude\":-46.63,\"city\":\"Sao Paulo\"}";
        const string refused = "{\"success\":false,\"message\":\"Rate limit reached\"}";

        Assert.Equal(new WeatherPlace(string.Empty, -23.55, -46.63), OpenMeteoParser.ParseIpLocation(ok));
        Assert.Null(OpenMeteoParser.ParseIpLocation(refused));
    }

    [Fact]
    public void Coordinates_outside_the_earth_are_rejected()
    {
        Assert.Null(OpenMeteoParser.ParseIpLocation("{\"success\":true,\"latitude\":120,\"longitude\":0}"));
        Assert.Null(OpenMeteoParser.ParseGeocoding("{\"results\":[{\"latitude\":0,\"longitude\":-200}]}", "X"));
    }
}

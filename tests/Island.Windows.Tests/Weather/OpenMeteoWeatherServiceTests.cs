using System.Net;
using System.Text;
using Island.Core.Idle;
using Island.Windows.Weather;

namespace Island.Windows.Tests.Weather;

public class OpenMeteoWeatherServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 14, 15, 0, TimeSpan.FromHours(-3));

    private const string ForecastReply =
        "{\"current\":{\"time\":\"2026-10-10T14:15\",\"temperature_2m\":22.5,\"weather_code\":61},"
        + "\"hourly\":{\"time\":[\"2026-10-10T14:00\",\"2026-10-10T15:00\",\"2026-10-10T16:00\"],"
        + "\"precipitation_probability\":[30,70,10]}}";

    private sealed class ScriptedHandler(Func<Uri, (HttpStatusCode Status, string Body)> reply) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            // AbsoluteUri keeps the escaping that is sent on the wire; ToString() unescapes it.
            Urls.Add(uri.AbsoluteUri);
            (HttpStatusCode status, string body) = reply(uri);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static (OpenMeteoWeatherService Service, ScriptedHandler Handler) Create(Func<Uri, (HttpStatusCode, string)> reply)
    {
        var handler = new ScriptedHandler(reply);
        var service = new OpenMeteoWeatherService(new HttpClient(handler), () => Now);
        return (service, handler);
    }

    [Fact]
    public async Task A_blank_city_asks_the_IP_location_service_over_https()
    {
        var (service, handler) = Create(_ => (HttpStatusCode.OK, "{\"success\":true,\"latitude\":-8.05,\"longitude\":-34.9}"));

        WeatherPlace? place = await service.ResolvePlaceAsync("  ", CancellationToken.None);

        Assert.Equal(new WeatherPlace(string.Empty, -8.05, -34.9), place);
        Assert.Equal(new[] { "https://ipwho.is/" }, handler.Urls);
    }

    [Fact]
    public async Task A_city_is_geocoded_with_its_name_escaped()
    {
        var (service, handler) = Create(_ => (HttpStatusCode.OK, "{\"results\":[{\"latitude\":-22.9,\"longitude\":-43.2}]}"));

        WeatherPlace? place = await service.ResolvePlaceAsync("Rio de Janeiro", CancellationToken.None);

        Assert.Equal(new WeatherPlace("Rio de Janeiro", -22.9, -43.2), place);
        Assert.StartsWith("https://geocoding-api.open-meteo.com/v1/search?name=Rio%20de%20Janeiro", handler.Urls.Single());
    }

    [Fact]
    public async Task The_forecast_request_asks_for_the_fields_the_island_shows()
    {
        var (service, handler) = Create(_ => (HttpStatusCode.OK, ForecastReply));

        WeatherSnapshot? snapshot = await service.FetchAsync(new WeatherPlace("", -22.9, -43.2), CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(22.5, snapshot.TemperatureC, precision: 6);
        Assert.Equal(70, snapshot.RainChancePercent);
        Assert.Equal(Now, snapshot.FetchedAt);
        string url = handler.Urls.Single();
        Assert.StartsWith("https://api.open-meteo.com/v1/forecast?", url);
        Assert.Contains("latitude=-22.9&longitude=-43.2", url);
        Assert.Contains("current=temperature_2m,weather_code", url);
        Assert.Contains("hourly=precipitation_probability", url);
        Assert.Contains("timezone=auto", url);
    }

    [Fact]
    public async Task A_server_error_is_a_null_result_not_an_exception()
    {
        var (service, _) = Create(_ => (HttpStatusCode.InternalServerError, "oops"));

        Assert.Null(await service.FetchAsync(new WeatherPlace("", 0, 0), CancellationToken.None));
        Assert.Null(await service.ResolvePlaceAsync("Recife", CancellationToken.None));
    }

    [Fact]
    public async Task A_refused_IP_lookup_is_a_null_result()
    {
        var (service, _) = Create(_ => (HttpStatusCode.OK, "{\"success\":false,\"message\":\"limit\"}"));

        Assert.Null(await service.ResolvePlaceAsync("", CancellationToken.None));
    }

    [Fact]
    public async Task A_city_nobody_knows_is_a_null_result()
    {
        var (service, _) = Create(_ => (HttpStatusCode.OK, "{\"generationtime_ms\":0.1}"));

        Assert.Null(await service.ResolvePlaceAsync("Atlantis", CancellationToken.None));
    }
}

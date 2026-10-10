using System.Globalization;
using Island.Core.Abstractions;
using Island.Core.Idle;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Weather;

/// <summary>
/// Weather from Open-Meteo (no API key). The city is geocoded by Open-Meteo; without a city the approximate location
/// comes from ipwho.is. Every request is HTTPS and goes through one reused HttpClient with a short timeout.
/// Failures return null and are logged; they never throw.
/// </summary>
public sealed class OpenMeteoWeatherService : IWeatherService
{
    internal const string ForecastUrl = "https://api.open-meteo.com/v1/forecast";
    internal const string GeocodingUrl = "https://geocoding-api.open-meteo.com/v1/search";
    internal const string IpLocationUrl = "https://ipwho.is/";

    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger? _log;

    public OpenMeteoWeatherService(HttpClient http, Func<DateTimeOffset> clock, ILogger? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log;
    }

    /// <summary>One client for the app's life. The timeout is short: the widget can wait for the next attempt.</summary>
    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland");
        return client;
    }

    public async Task<WeatherPlace?> ResolvePlaceAsync(string city, CancellationToken cancellationToken)
    {
        string name = (city ?? string.Empty).Trim();
        try
        {
            if (name.Length == 0)
            {
                string ip = await _http.GetStringAsync(IpLocationUrl, cancellationToken).ConfigureAwait(false);
                return OpenMeteoParser.ParseIpLocation(ip);
            }

            string url = $"{GeocodingUrl}?name={Uri.EscapeDataString(name)}&count=1&language=pt&format=json";
            string json = await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            WeatherPlace? place = OpenMeteoParser.ParseGeocoding(json, name);
            if (place is null) _log?.LogWarning("Weather city not found: {City}", name);
            return place;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _log?.LogWarning("Weather location lookup failed: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<WeatherSnapshot?> FetchAsync(WeatherPlace place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(place);
        string latitude = place.Latitude.ToString(CultureInfo.InvariantCulture);
        string longitude = place.Longitude.ToString(CultureInfo.InvariantCulture);
        string url = $"{ForecastUrl}?latitude={latitude}&longitude={longitude}"
                     + "&current=temperature_2m,weather_code&hourly=precipitation_probability&forecast_days=1&timezone=auto";
        try
        {
            string json = await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            WeatherSnapshot? snapshot = OpenMeteoParser.ParseForecast(json, _clock());
            if (snapshot is null) _log?.LogWarning("Weather reply could not be read");
            return snapshot;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _log?.LogWarning("Weather request failed: {Message}", ex.Message);
            return null;
        }
    }
}

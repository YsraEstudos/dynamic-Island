using System.Text.Json;
using Island.Core.Idle;

namespace Island.Windows.Weather;

/// <summary>
/// Reads the three Open-Meteo answers the weather uses (forecast, geocoding, IP location). Pure: text in, values out.
/// Anything missing or out of range is null, so a changed or broken answer is a failed read, not a wrong figure.
/// </summary>
public static class OpenMeteoParser
{
    /// <summary>The rain chance covers the current hour and the next two.</summary>
    public const int RainWindowHours = 3;

    /// <summary>Current temperature and weather code, plus the rain chance of the coming hours.</summary>
    /// <param name="json">The forecast answer, requested with current=temperature_2m,weather_code and hourly=precipitation_probability.</param>
    /// <param name="fetchedAt">Stamped on the snapshot.</param>
    public static WeatherSnapshot? ParseForecast(string json, DateTimeOffset fetchedAt)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            JsonElement current = root.GetProperty("current");
            if (!current.GetProperty("temperature_2m").TryGetDouble(out double temperature) || !double.IsFinite(temperature)) return null;
            if (!current.GetProperty("weather_code").TryGetInt32(out int code)) return null;
            string? now = current.GetProperty("time").GetString();
            if (now is null || now.Length < 13) return null;

            JsonElement hourly = root.GetProperty("hourly");
            JsonElement times = hourly.GetProperty("time");
            JsonElement chances = hourly.GetProperty("precipitation_probability");
            int length = Math.Min(times.GetArrayLength(), chances.GetArrayLength());

            // "2026-10-10T14:15" names the hour "2026-10-10T14"; the hourly times are "2026-10-10T14:00".
            string hour = now[..13];
            int start = -1;
            for (int i = 0; i < length; i++)
            {
                if (times[i].GetString() is { } t && t.StartsWith(hour, StringComparison.Ordinal))
                {
                    start = i;
                    break;
                }
            }
            if (start < 0) return null;

            // The forecast ends at midnight (forecast_days=1), so the window is cut short late in the day.
            int rain = -1;
            for (int i = start; i < Math.Min(start + RainWindowHours, length); i++)
            {
                // A missing hour is JSON null, which TryGetDouble rejects by throwing: check the kind first.
                if (chances[i].ValueKind == JsonValueKind.Number && chances[i].TryGetDouble(out double chance) && double.IsFinite(chance))
                {
                    rain = Math.Max(rain, (int)Math.Round(chance));
                }
            }
            if (rain < 0) return null;

            return new WeatherSnapshot(temperature, code, Math.Clamp(rain, 0, 100), fetchedAt);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The first match of a city name. Null when the name is not found.</summary>
    public static WeatherPlace? ParseGeocoding(string json, string query)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out JsonElement results)) return null;
            if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0) return null;

            JsonElement first = results[0];
            return PlaceFrom(first, query);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The approximate location of this PC. The place has an empty query, which is how the cache knows it is the IP place.</summary>
    public static WeatherPlace? ParseIpLocation(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("success", out JsonElement success) || success.ValueKind != JsonValueKind.True) return null;

            return PlaceFrom(root, string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static WeatherPlace? PlaceFrom(JsonElement element, string query)
    {
        if (!element.TryGetProperty("latitude", out JsonElement latitude) || !latitude.TryGetDouble(out double lat)) return null;
        if (!element.TryGetProperty("longitude", out JsonElement longitude) || !longitude.TryGetDouble(out double lon)) return null;
        if (!double.IsFinite(lat) || !double.IsFinite(lon)) return null;
        if (lat is < -90 or > 90 || lon is < -180 or > 180) return null;

        return new WeatherPlace(query, lat, lon);
    }
}

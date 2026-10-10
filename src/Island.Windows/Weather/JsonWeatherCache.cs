using System.Text.Json;
using Island.Core.Abstractions;
using Island.Core.Idle;

namespace Island.Windows.Weather;

/// <summary>
/// Last weather reading and place in %LocalAppData%\DynamicIsland\weather.json, written atomically (.tmp + move).
/// A missing or unreadable file loads as empty; it never throws on load.
/// </summary>
public sealed class JsonWeatherCache : IWeatherCache
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;
    private readonly string _tempPath;
    private readonly string _directory;
    private readonly object _gate = new();

    /// <param name="directory">Defaults to %LocalAppData%\DynamicIsland.</param>
    public JsonWeatherCache(string? directory = null)
    {
        _directory = directory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");
        _path = Path.Combine(_directory, "weather.json");
        _tempPath = _path + ".tmp";
    }

    public WeatherCacheData Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return WeatherCacheData.Empty;
                WeatherCacheData? parsed = JsonSerializer.Deserialize<WeatherCacheData>(File.ReadAllText(_path), Options);
                return parsed ?? WeatherCacheData.Empty;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                return WeatherCacheData.Empty;
            }
        }
    }

    /// <summary>Throws on I/O failure; the refresher treats that as "not saved", which only costs the restart shortcut.</summary>
    public void Save(WeatherCacheData data)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(_tempPath, JsonSerializer.Serialize(data, Options));
            File.Move(_tempPath, _path, overwrite: true);
        }
    }
}

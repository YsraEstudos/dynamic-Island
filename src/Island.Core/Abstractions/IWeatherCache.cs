using Island.Core.Idle;

namespace Island.Core.Abstractions;

/// <summary>Last weather reading and place, kept across runs so the app can show something before the network answers.</summary>
public interface IWeatherCache
{
    /// <summary>Returns <see cref="WeatherCacheData.Empty"/> when nothing usable is stored.</summary>
    WeatherCacheData Load();

    void Save(WeatherCacheData data);
}

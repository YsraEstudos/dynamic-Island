using Island.Core.Abstractions;
using Island.Core.Idle;

namespace Island.Core.Fakes;

/// <summary>Demo cache: keeps the data in memory, so a demo run never writes to %LocalAppData%.</summary>
public sealed class InMemoryWeatherCache : IWeatherCache
{
    private readonly object _gate = new();
    private WeatherCacheData _data = WeatherCacheData.Empty;

    public WeatherCacheData Load()
    {
        lock (_gate) return _data;
    }

    public void Save(WeatherCacheData data)
    {
        lock (_gate) _data = data;
    }
}

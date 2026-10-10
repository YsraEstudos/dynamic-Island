using Island.Core.Abstractions;
using Island.Core.Idle;

namespace Island.Core.Fakes;

/// <summary>Demo weather: a fixed reading with no network. Counts the reads, so tests can check the refresh rate.</summary>
public sealed class FakeWeatherService : IWeatherService
{
    public int ResolveCalls { get; private set; }
    public int FetchCalls { get; private set; }

    public Task<WeatherPlace?> ResolvePlaceAsync(string city, CancellationToken cancellationToken)
    {
        ResolveCalls++;
        return Task.FromResult<WeatherPlace?>(new WeatherPlace((city ?? string.Empty).Trim(), -23.55, -46.63));
    }

    public Task<WeatherSnapshot?> FetchAsync(WeatherPlace place, CancellationToken cancellationToken)
    {
        FetchCalls++;
        return Task.FromResult<WeatherSnapshot?>(new WeatherSnapshot(23.4, 2, 40, DateTimeOffset.Now));
    }
}

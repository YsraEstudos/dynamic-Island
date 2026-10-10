using Island.Core.Idle;

namespace Island.Core.Abstractions;

/// <summary>Reads the weather from the network. Implementations never throw: every failure is a null result.</summary>
public interface IWeatherService
{
    /// <summary>The place to forecast: the city named, or the approximate location of this PC when <paramref name="city"/> is blank.</summary>
    Task<WeatherPlace?> ResolvePlaceAsync(string city, CancellationToken cancellationToken);

    /// <summary>Current conditions at <paramref name="place"/>. Null when the reading is unavailable.</summary>
    Task<WeatherSnapshot?> FetchAsync(WeatherPlace place, CancellationToken cancellationToken);
}

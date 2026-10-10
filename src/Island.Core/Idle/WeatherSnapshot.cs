namespace Island.Core.Idle;

/// <summary>Current conditions of one place, read at <paramref name="FetchedAt"/>.</summary>
/// <param name="TemperatureC">Air temperature at 2 m, in degrees Celsius.</param>
/// <param name="WeatherCode">WMO weather code.</param>
/// <param name="RainChancePercent">Chance of precipitation, 0 to 100: the highest value of the current hour and the next two.</param>
/// <param name="FetchedAt">When the reading was taken. Drives freshness, not the display.</param>
public sealed record WeatherSnapshot(double TemperatureC, int WeatherCode, int RainChancePercent, DateTimeOffset FetchedAt);

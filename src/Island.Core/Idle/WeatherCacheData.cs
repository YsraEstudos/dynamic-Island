namespace Island.Core.Idle;

/// <summary>What the weather cache keeps between runs: the last reading and the place it was read for.</summary>
public sealed record WeatherCacheData(WeatherSnapshot? Snapshot = null, WeatherPlace? Place = null)
{
    public static WeatherCacheData Empty { get; } = new();
}
